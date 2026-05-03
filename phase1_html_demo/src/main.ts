// The Guild — Main Entry Point
// Wires all systems together and starts the game loop.

import './styles/main.css'

import { createResourceState, addGold, changeReputation } from './systems/resource'
import { createStartingAdventurers, markIdle, markDead, generateCandidatePool, recruitCost, ROSTER_CAP } from './systems/adventurer'
import { createGuildState, canUpgrade } from './systems/guild'
import { tryDispatch, tryPartyDispatch, calcRates } from './systems/dispatch'
import { createOutcomeState, resolveOutcome, dismissAll, applyXPGain } from './systems/outcome'
import { buildOfflineReturnEvent } from './systems/offline-return'
import type { SettlementRecord } from './systems/outcome'
import { generateMissionPool } from './data/missions'
import { createTickState, startTick, processOfflineProgress } from './systems/tick'
import { saveGame, loadGame, deleteSave } from './systems/save-load'
import { initGuildHall, renderGuildHall, pushMessage, showOfflineReturnOverlay } from './ui/guild-hall-scene'
import { showMainMenu, hideMainMenu } from './ui/main-menu'
import { UI_STRINGS } from './ui/ui-strings'

import type { OutcomeResult, GuildState, Adventurer, Mission } from './types'
import type { TickState } from './systems/tick'
import type { OutcomeState } from './systems/outcome'

// ─── CHEAT STATE ─────────────────────────────────────────────────────────────

let _forceAccept = false

// ─── GAME STATE ──────────────────────────────────────────────────────────────

interface AppState {
  guild: GuildState
  guildLevel: number
  outcomeState: OutcomeState
  tickState: TickState
  stopTick: (() => void) | null
  pendingReview: Mission[]           // 待審核委託池
  candidateAdventurers: Adventurer[] // 招募候選人
  messageLog: string[]               // 訊息欄（最多4則）
}

const MAX_MSG_LINES = 4

let app: AppState
// Settlements collected during the offline sweep (cleared after offline overlay shown)
let _offlineSettlements: SettlementRecord[] = []

function createFreshState(): AppState {
  const adventurers = createStartingAdventurers()
  const guildCoreState = createGuildState()
  const resources = createResourceState()
  const guild: GuildState = {
    resources,
    adventurers,
    missionPool: [],
    pendingReview: [],
    activeMissions: [],
    completedMissionIds: new Set(),
    guildLevel: guildCoreState.guildLevel,
    worldDanger: 'E',
  }

  // New missions go into pendingReview (not directly into missionPool)
  guild.pendingReview = generateMissionPool(6, guild.completedMissionIds, new Set(), guild.worldDanger, guild.resources.reputation)

  return {
    guild,
    guildLevel: guildCoreState.guildLevel,
    outcomeState: createOutcomeState(),
    tickState: createTickState(),
    stopTick: null,
    pendingReview: guild.pendingReview,
    candidateAdventurers: [],
    messageLog: [],
  }
}

// ─── OUTCOME HANDLER ─────────────────────────────────────────────────────────

function handleExpiredMissions(expiredIds: string[]) {
  for (const id of expiredIds) {
    const record = app.guild.activeMissions.find(r => r.id === id)
    if (!record) continue

    // Search both missionPool and pendingReview for the mission data
    const mission =
      app.guild.missionPool.find(m => m.id === record.missionId) ??
      app.guild.pendingReview.find(m => m.id === record.missionId)
    const difficulty = mission?.difficulty ?? 'F'
    const baseReward = mission?.baseReward ?? 0

    const settlement = resolveOutcome(record, difficulty, baseReward)
    app.outcomeState.pendingResults.push(settlement)
    _offlineSettlements.push(settlement)

    // Apply gold delta
    addGold(app.guild.resources, settlement.goldDelta)

    // Apply reputation delta
    changeReputation(app.guild.resources, settlement.reputationDelta)

    // Update all party members status + apply XP gain
    const partyIds = record.partyMembers.length > 1
      ? record.partyMembers.map(m => m.id)
      : [record.adventurerId]

    // On DEATH/PYRRHIC: one random member dies, others go idle
    const dyingMemberId = (settlement.outcome === 'DEATH' || settlement.outcome === 'PYRRHIC')
      ? partyIds[Math.floor(Math.random() * partyIds.length)]
      : null

    const now = Date.now()
    for (const memberId of partyIds) {
      const member = app.guild.adventurers.find(a => a.id === memberId)
      if (!member) continue

      const isDying = memberId === dyingMemberId
      const survivorOutcome = (settlement.outcome === 'SUCCESS' || settlement.outcome === 'PYRRHIC') ? 'SUCCESS' : 'FAILURE'
      const memberOutcome = isDying ? settlement.outcome : survivorOutcome

      const xpResult = applyXPGain(member, memberOutcome)
      member.xp = xpResult.newXP
      if (xpResult.newTraitIds.length > 0) {
        xpResult.newTraitIds.forEach(id => {
          member.growthTraits.push({ traitId: id, unlockedAt: now })
        })
        addMessage(`${member.name} 解鎖新特性！`)
      }

      if (isDying) {
        markDead(member)
      } else {
        markIdle(member)
      }
    }

    // Bug 1 fix: remove dead adventurers from roster
    app.guild.adventurers = app.guild.adventurers.filter(a => a.status !== 'dead')

    // Remove from active missions
    app.guild.activeMissions = app.guild.activeMissions.filter(r => r.id !== id)

    // Push settlement message
    const advName = record.adventurerName
    const missionName = record.missionName
    const gold = settlement.goldDelta
    let msg: string
    switch (settlement.outcome) {
      case 'SUCCESS':
        msg = UI_STRINGS.msgMissionSuccess
          .replace('{adventurer}', advName)
          .replace('{mission}', missionName)
          .replace('{gold}', String(gold))
        break
      case 'FAILURE':
        msg = UI_STRINGS.msgMissionFail
          .replace('{adventurer}', advName)
          .replace('{mission}', missionName)
          .replace('{gold}', String(gold))
        break
      case 'DEATH':
        msg = UI_STRINGS.msgMissionDeath
          .replace('{adventurer}', advName)
          .replace('{mission}', missionName)
        break
      case 'PYRRHIC':
        msg = UI_STRINGS.msgMissionPyrrhic
          .replace('{adventurer}', advName)
          .replace('{mission}', missionName)
        break
      default:
        msg = ''
    }
    if (msg) addMessage(msg)
  }

  // Check guild upgrade eligibility
  if (canUpgrade(app.guild.guildLevel, app.guild.resources.reputation)) {
    app.guild.guildLevel = Math.min(5, app.guild.guildLevel + 1)
  }

  renderGame()
}

function handleCandidatePoolRefresh() {
  // New missions append to pendingReview (not replacing missionPool)
  const newMissions = generateMissionPool(
    6,
    app.guild.completedMissionIds,
    new Set(app.guild.activeMissions.map(r => r.missionId)),
    app.guild.worldDanger,
    app.guild.resources.reputation,
  )
  app.guild.pendingReview = [...app.guild.pendingReview, ...newMissions]

  // Refresh candidate adventurers too
  app.candidateAdventurers = generateCandidatePool(app.guild.guildLevel)

  renderGame()
}

// ─── MESSAGE LOG ─────────────────────────────────────────────────────────────

function addMessage(msg: string): void {
  app.messageLog.unshift(msg)
  if (app.messageLog.length > MAX_MSG_LINES) {
    app.messageLog = app.messageLog.slice(0, MAX_MSG_LINES)
  }
  pushMessage(msg)
}

// ─── RENDER ──────────────────────────────────────────────────────────────────

function renderGame() {
  // Convert SettlementRecord[] → OutcomeResult[] for guild hall
  const pendingResults: OutcomeResult[] = app.outcomeState.pendingResults.map(s => ({
    type: s.outcome,
    record: app.guild.activeMissions.find(r => r.id === s.dispatchId) ?? {
      id: s.dispatchId,
      missionId: s.missionId,
      missionName: '',
      adventurerId: s.adventurerId,
      adventurerName: app.guild.adventurers.find(a => a.id === s.adventurerId)?.name ?? '???',
      partyMembers: [],
      activeSynergies: [],
      startTimestamp: 0,
      endTimestamp: s.resolvedAt,
      finalSuccessRate: 0,
      finalDeathRate: 0,
      preCollectedAmount: s.preCollectedAmount,
    },
    goldDelta: s.goldDelta,
    reputationDelta: s.reputationDelta,
    message: outcomeMessage(s.outcome, s.goldDelta),
  }))

  renderGuildHall({
    gold: app.guild.resources.gold,
    pendingGold: app.guild.activeMissions.reduce(
      (sum, r) => sum + r.preCollectedAmount + Math.floor(r.preCollectedAmount * 0.10),
      0
    ),
    reputation: app.guild.resources.reputation,
    guildLevel: app.guild.guildLevel,
    worldDanger: app.guild.worldDanger,
    commissions: app.guild.missionPool,
    pendingReview: app.guild.pendingReview,
    roster: app.guild.adventurers,
    activeMissions: app.guild.activeMissions,
    pendingResults,
    candidateAdventurers: app.candidateAdventurers,
    messageLog: app.messageLog,
  })
}

function outcomeMessage(outcome: string, goldDelta: number): string {
  switch (outcome) {
    case 'SUCCESS':  return UI_STRINGS.outcomeSuccess.replace('{gold}', String(goldDelta))
    case 'PYRRHIC':  return UI_STRINGS.outcomePyrrhic.replace('{gold}', String(goldDelta))
    case 'FAILURE':  return UI_STRINGS.outcomeFailure.replace('{gold}', String(Math.abs(goldDelta)))
    case 'DEATH':    return UI_STRINGS.outcomeDeath.replace('{gold}', String(Math.abs(goldDelta)))
    default:         return ''
  }
}

// ─── GUILD HALL CALLBACKS ─────────────────────────────────────────────────────

function onDispatch(missionId: string, adventurerId: string) {
  const mission = app.guild.missionPool.find(m => m.id === missionId)
  const adv = app.guild.adventurers.find(a => a.id === adventurerId)
  if (!mission || !adv) return

  const result = tryDispatch(adv, mission, app.guild.activeMissions, app.guild.guildLevel, { forceAccept: _forceAccept })
  if (result.accepted) {
    app.guild.activeMissions.push(result.record)
    app.guild.missionPool = app.guild.missionPool.filter(m => m.id !== missionId)
    app.guild.completedMissionIds.add(missionId)

    // Push dispatch message
    const msg = UI_STRINGS.msgDispatchSent
      .replace('{adventurer}', adv.name)
      .replace('{mission}', mission.name)
    addMessage(msg)
  } else {
    let rejectMsg: string
    if (result.reason === 'capacity') {
      rejectMsg = UI_STRINGS.rejectCapacity.replace('{name}', adv.name)
    } else if (result.reason === 'willingness') {
      const { finalSuccessRate, finalDeathRate } = calcRates(adv, mission)
      const sRate = Math.round(finalSuccessRate * 100)
      const dRate = Math.round(finalDeathRate * 100)
      const highDeath   = finalDeathRate >= 0.30
      const lowSuccess  = finalSuccessRate < 0.45
      let reason: string
      if (highDeath && lowSuccess) reason = UI_STRINGS.rejectWillingnessAll.replace('{rate}', String(sRate)).replace('{drate}', String(dRate))
      else if (highDeath)          reason = UI_STRINGS.rejectWillingnessDeath.replace('{drate}', String(dRate))
      else if (lowSuccess)         reason = UI_STRINGS.rejectWillingnessSuccess.replace('{rate}', String(sRate))
      else                         reason = UI_STRINGS.rejectWillingnessWeak.replace('{rate}', String(sRate)).replace('{drate}', String(dRate))
      rejectMsg = UI_STRINGS.rejectRefused.replace('{name}', adv.name).replace('{mission}', mission.name).replace('{reason}', reason)
    } else {
      rejectMsg = UI_STRINGS.rejectLevelMismatch.replace('{name}', adv.name).replace('{mission}', mission.name)
    }
    addMessage(rejectMsg)
  }
  renderGame()
}

function onPartyDispatch(missionId: string, adventurerIds: string[]) {
  const mission = app.guild.missionPool.find(m => m.id === missionId)
  const members = adventurerIds.map(id => app.guild.adventurers.find(a => a.id === id)).filter(Boolean) as typeof app.guild.adventurers
  if (!mission || members.length === 0) return

  const result = tryPartyDispatch(members, mission, app.guild.activeMissions, app.guild.guildLevel, { forceAccept: _forceAccept })
  if (result.accepted) {
    app.guild.activeMissions.push(result.record)
    app.guild.missionPool = app.guild.missionPool.filter(m => m.id !== missionId)
    app.guild.completedMissionIds.add(missionId)

    const names = members.map(m => m.name).join('、')
    addMessage(UI_STRINGS.msgPartyDispatchSent.replace('{names}', names).replace('{mission}', mission.name))
    if (result.record.activeSynergies.length > 0) {
      addMessage(UI_STRINGS.msgPartySynergy.replace('{synergies}', result.record.activeSynergies.join('、')))
    }
  } else {
    const names = members.map(m => m.name).join('、')
    let rejectMsg: string
    if (result.reason === 'capacity') {
      rejectMsg = UI_STRINGS.rejectPartyCapacity.replace('{names}', names)
    } else if (result.reason === 'party_too_large') {
      rejectMsg = UI_STRINGS.rejectPartyTooLarge.replace('{mission}', mission.name)
    } else if (result.reason === 'members_busy') {
      rejectMsg = UI_STRINGS.rejectPartyMemberBusy
    } else {
      // willingness — use lead adventurer's rates as representative
      const lead = members[0]
      const { finalSuccessRate, finalDeathRate } = calcRates(lead, mission)
      const sRate = Math.round(finalSuccessRate * 100)
      const dRate = Math.round(finalDeathRate * 100)
      const highDeath   = finalDeathRate >= 0.30
      const lowSuccess  = finalSuccessRate < 0.45
      let reason: string
      if (highDeath && lowSuccess) reason = UI_STRINGS.rejectWillingnessAll.replace('{rate}', String(sRate)).replace('{drate}', String(dRate))
      else if (highDeath)          reason = UI_STRINGS.rejectWillingnessDeath.replace('{drate}', String(dRate))
      else if (lowSuccess)         reason = UI_STRINGS.rejectWillingnessSuccess.replace('{rate}', String(sRate))
      else                         reason = UI_STRINGS.rejectWillingnessWeak.replace('{rate}', String(sRate)).replace('{drate}', String(dRate))
      rejectMsg = UI_STRINGS.rejectPartyRefused.replace('{names}', names).replace('{mission}', mission.name).replace('{reason}', reason)
    }
    addMessage(rejectMsg)
  }
  renderGame()
}

function onReviewAccept(missionId: string) {
  const mission = app.guild.pendingReview.find(m => m.id === missionId)
  if (!mission) return
  // Move from pendingReview to missionPool
  app.guild.pendingReview = app.guild.pendingReview.filter(m => m.id !== missionId)
  app.guild.missionPool.push(mission)
  // Commission Flow: collect full reward when commission is posted (GDD §觸發點 1)
  addGold(app.guild.resources, mission.baseReward)
  // Record when this commission was posted to the board (used by UI for "新" badge)
  mission.postedAt = Date.now()
  renderGame()
}

function onReviewReject(missionId: string) {
  app.guild.pendingReview = app.guild.pendingReview.filter(m => m.id !== missionId)
  renderGame()
}

function onRecruit(adventurerId: string) {
  const idx = app.candidateAdventurers.findIndex(a => a.id === adventurerId)
  if (idx < 0) return
  const adv = app.candidateAdventurers[idx]

  // Check roster cap (ROSTER_CAP imported from systems/adventurer)
  if (app.guild.adventurers.filter(a => a.status !== 'dead').length >= ROSTER_CAP) return

  // Deduct recruit cost — use the cost snapshotted at pool-generation time so
  // the value shown in the UI and the amount actually deducted are always identical.
  const cost = adv.fixedRecruitCost ?? recruitCost(adv.rank)
  if (app.guild.resources.gold < cost) return

  addGold(app.guild.resources, -cost)
  app.candidateAdventurers = app.candidateAdventurers.filter(a => a.id !== adventurerId)
  app.guild.adventurers.push(adv)
  renderGame()
}

function onFire(adventurerId: string) {
  const adv = app.guild.adventurers.find(a => a.id === adventurerId)
  if (!adv) return
  // Cannot fire adventurers on missions
  if (adv.status === 'on_mission') return
  app.guild.adventurers = app.guild.adventurers.filter(a => a.id !== adventurerId)
  renderGame()
}

function onSettlementDismiss() {
  dismissAll(app.outcomeState)
  renderGame()
}

function onSave() {
  app.tickState.lastSavedAt = Date.now()
  saveGame(app.guild, app.outcomeState.pendingResults, app.tickState, app.messageLog)
  console.log('[save] Game saved at', new Date().toLocaleTimeString())
}

function onExport() {
  import('./systems/save-load').then(({ exportSave }) => {
    exportSave(app.guild, app.outcomeState.pendingResults, app.tickState, app.messageLog)
  })
}

function onImport(file: File) {
  import('./systems/save-load').then(({ importSave }) => {
    importSave(file).then(data => {
      if (!data) return
      // Restart with loaded data
      if (app.stopTick) app.stopTick()
      app = createFreshState()
      app.guild.resources = data.resources
      app.guild.adventurers = data.adventurers
      app.guild.missionPool = data.missionPool
      app.guild.pendingReview = data.pendingReview ?? []
      app.guild.activeMissions = data.activeMissions
      app.guild.completedMissionIds = new Set(data.completedMissionIds)
      app.guild.guildLevel = data.guildLevel
      app.tickState.candidatePoolNextRefreshAt = data.candidatePoolNextRefreshAt
      app.tickState.lastSavedAt = data.lastSavedAt
      app.messageLog = data.messageLog ?? []
      startGame()
    })
  })
}

// ─── CHEAT CALLBACKS ─────────────────────────────────────────────────────────

function advanceTimeBy(ms: number): void {
  app.guild.activeMissions.forEach(r => { r.endTimestamp -= ms })
  const expiredIds = app.guild.activeMissions
    .filter(r => r.endTimestamp <= Date.now())
    .map(r => r.id)
  if (expiredIds.length > 0) handleExpiredMissions(expiredIds)
  else renderGame()
}

const DANGER_LEVELS = ['E', 'D', 'C', 'B', 'A'] as const

const cheatCallbacks = {
  onAddGold100:    () => { addGold(app.guild.resources, 100);    renderGame() },
  onAddGold1000:   () => { addGold(app.guild.resources, 1000);   renderGame() },
  onRemoveGold100: () => { addGold(app.guild.resources, -100);   renderGame() },
  onRemoveGold1000:() => { addGold(app.guild.resources, -1000);  renderGame() },

  onRefreshAdventurers: () => {
    app.candidateAdventurers = generateCandidatePool(app.guild.guildLevel)
    renderGame()
  },

  onRefreshMissions: () => { handleCandidatePoolRefresh() },

  onToggleForceAccept: (enabled: boolean) => { _forceAccept = enabled },

  onAdvanceTime30m: () => { advanceTimeBy(30 * 60 * 1000) },

  onAdvanceTime1h: () => { advanceTimeBy(60 * 60 * 1000) },

  onAdvanceTime5h: () => { advanceTimeBy(5 * 60 * 60 * 1000) },

  onIncreaseWorldDanger: () => {
    const idx = DANGER_LEVELS.indexOf(app.guild.worldDanger as typeof DANGER_LEVELS[number])
    if (idx < DANGER_LEVELS.length - 1) app.guild.worldDanger = DANGER_LEVELS[idx + 1]
    renderGame()
  },

  onDecreaseWorldDanger: () => {
    const idx = DANGER_LEVELS.indexOf(app.guild.worldDanger as typeof DANGER_LEVELS[number])
    if (idx > 0) app.guild.worldDanger = DANGER_LEVELS[idx - 1]
    renderGame()
  },

  onCompleteAllMissions: () => {
    // Move all active mission endTimestamps to now-1 so they expire immediately
    const now = Date.now()
    app.guild.activeMissions.forEach(r => { r.endTimestamp = now - 1 })
    const expiredIds = app.guild.activeMissions.map(r => r.id)
    if (expiredIds.length > 0) handleExpiredMissions(expiredIds)
    else renderGame()
  },

  onFailAllMissions: () => {
    const now = Date.now()
    // Force success=0 death=0 so resolveOutcome always produces FAILURE
    app.guild.activeMissions.forEach(r => {
      r.endTimestamp = now - 1
      r.finalSuccessRate = 0
      r.finalDeathRate = 0
    })
    const expiredIds = app.guild.activeMissions.map(r => r.id)
    if (expiredIds.length > 0) handleExpiredMissions(expiredIds)
    else renderGame()
  },

  onIncreaseReputation: () => {
    const TIERS = [81, 61, 41, 21, 0, -10, -30, -55, -80, -81]
    const rep = app.guild.resources.reputation
    const idx = TIERS.findIndex(t => rep >= t)
    const current = idx === -1 ? TIERS.length - 1 : idx
    if (current > 0) {
      app.guild.resources.reputation = TIERS[current - 1]
    }
    renderGame()
  },

  onDecreaseReputation: () => {
    const TIERS = [81, 61, 41, 21, 0, -10, -30, -55, -80, -81]
    const rep = app.guild.resources.reputation
    const idx = TIERS.findIndex(t => rep >= t)
    const current = idx === -1 ? TIERS.length - 1 : idx
    if (current < TIERS.length - 1) {
      app.guild.resources.reputation = TIERS[current + 1]
    }
    renderGame()
  },
}

// ─── INIT ────────────────────────────────────────────────────────────────────

function startGame() {
  // Snapshot idle adventurers BEFORE processOfflineProgress fires handleExpiredMissions
  // (which resets returning adventurers back to 'idle', causing false positives in NPC sim)
  const idleSnapshot = app.guild.adventurers.filter(a => a.status === 'idle').map(a => ({ ...a }))

  // Process offline progress first — collect settlements during sweep
  _offlineSettlements = []
  const offlineResult = processOfflineProgress(app.tickState, app.guild.activeMissions, {
    onMissionsExpired: handleExpiredMissions,
    onCandidatePoolRefresh: handleCandidatePoolRefresh,
  })

  // If offline long enough, build and show the offline return overlay
  if (offlineResult) {
    // Filter out any adventurer whose mission was resolved during the sweep above
    const settledAdvIds = new Set(_offlineSettlements.map(s => s.adventurerId))
    const idleAdventurers = idleSnapshot.filter(a => !settledAdvIds.has(a.id))
    const allMissions = [...app.guild.missionPool]

    const event = buildOfflineReturnEvent({
      progressResult: offlineResult,
      settledRecords: [..._offlineSettlements],
      lookupNames: (missionId, adventurerId) => {
        const mission =
          app.guild.missionPool.find(m => m.id === missionId) ??
          app.guild.pendingReview.find(m => m.id === missionId)
        const adv = app.guild.adventurers.find(a => a.id === adventurerId)
        return {
          missionName: mission?.name ?? '???',
          adventurerName: adv?.name ?? '???',
        }
      },
      idleAdventurers,
      missionPool: allMissions,
      guildLevel: app.guild.guildLevel,
    })

    // Apply bonus gold if earned
    if (event.bonusCommissionActive && event.bonusCommissionAmount > 0) {
      addGold(app.guild.resources, event.bonusCommissionAmount)
      addMessage(`信使獎勵：+${event.bonusCommissionAmount}g`)
    }

    // --- NPC autonomous missions: State 1 (still running) ---
    for (const record of event.npcDispatchRecords) {
      const adv = app.guild.adventurers.find(a => a.id === record.adventurerId)
      if (adv) {
        adv.status = 'on_mission'
        adv.currentMissionId = record.missionId
      }
      app.guild.activeMissions.push(record)
    }

    // --- NPC autonomous missions: State 2 (completed offline) ---
    for (const resolution of event.npcResolutions) {
      if (resolution.goldDelta > 0) {
        addGold(app.guild.resources, resolution.goldDelta)
      }
    }

    if (_offlineSettlements.length > 0 || event.npcResolutions.length > 0 || event.npcActiveMissions.length > 0) {
      showOfflineReturnOverlay(event)
    }
  }
  _offlineSettlements = []

  // Auto-save after offline sweep so lastSavedAt is persisted.
  // Prevents NPC simulation from re-triggering on every page refresh.
  saveGame(app.guild, app.outcomeState.pendingResults, app.tickState, app.messageLog)

  // Start foreground tick
  app.stopTick = startTick(app.tickState, app.guild.activeMissions, {
    onMissionsExpired: handleExpiredMissions,
    onCandidatePoolRefresh: handleCandidatePoolRefresh,
  })

  // Render loop: re-render every second so mission timers stay up-to-date
  const renderInterval = setInterval(renderGame, 1_000)

  // Commission expiry: check every minute, remove commissions older than 12 hours
  const COMMISSION_EXPIRY_MS = 12 * 60 * 60 * 1_000
  const expiryInterval = setInterval(() => {
    const now = Date.now()
    const expired = app.guild.missionPool.filter(m => m.postedAt && now - m.postedAt > COMMISSION_EXPIRY_MS)
    if (expired.length === 0) return
    expired.forEach(m => {
      addGold(app.guild.resources, -m.baseReward)
      addMessage(`委託「${m.name}」逾期未接，報酬已退還委託方`)
    })
    const expiredIds = new Set(expired.map(m => m.id))
    app.guild.missionPool = app.guild.missionPool.filter(m => !expiredIds.has(m.id))
    renderGame()
  }, 60_000)

  const _origStop = app.stopTick
  app.stopTick = () => { _origStop(); clearInterval(renderInterval); clearInterval(expiryInterval) }

  renderGame()
}

function applySaveData(saved: ReturnType<typeof loadGame>): void {
  if (!saved) return
  app.guild.resources = saved.resources
  app.guild.adventurers = saved.adventurers
  app.guild.missionPool = saved.missionPool
  app.guild.pendingReview = saved.pendingReview ?? []
  app.guild.activeMissions = saved.activeMissions
  app.guild.completedMissionIds = new Set(saved.completedMissionIds)
  app.guild.guildLevel = saved.guildLevel
  app.tickState.candidatePoolNextRefreshAt = saved.candidatePoolNextRefreshAt
  app.tickState.lastSavedAt = saved.lastSavedAt
  app.messageLog = saved.messageLog ?? []
  if (saved.pendingResults) {
    app.outcomeState.pendingResults = saved.pendingResults as any
  }
}

function init() {
  const appEl = document.getElementById('app')!

  // Initialise guild hall DOM (hidden until main menu is dismissed)
  initGuildHall(appEl, {
    onDispatch,
    onPartyDispatch,
    onReviewAccept,
    onReviewReject,
    onRecruit,
    onFire,
    onSettlementDismiss,
    onSave,
    onExport,
    onImport,
    cheatCallbacks,
  })

  const openMenu = () => {
    const hasSave = loadGame() !== null
    showMainMenu(appEl, {
      onNewGame: () => {
        hideMainMenu()
        deleteSave()
        app = createFreshState()
        app.candidateAdventurers = generateCandidatePool(app.guild.guildLevel)
        startGame()
      },
      onLoadGame: () => {
        hideMainMenu()
        const saved = loadGame()
        app = createFreshState()
        applySaveData(saved)
        startGame()
      },
      onImport: (file: File) => {
        hideMainMenu()
        app = createFreshState()
        import('./systems/save-load').then(({ importSave }) => {
          importSave(file).then(data => {
            if (!data) { openMenu(); return }
            applySaveData(data as any)
            startGame()
          })
        })
      },
    }, hasSave)
  }

  openMenu()
}

init()
