// ui-strings.ts — Main UI Shell all fixed text
// All UI text must be sourced from this file. No bare strings in component logic.

export const UI_STRINGS = {
  // Status bar
  statusGold:          '金幣',
  statusPending:       '待結清',
  statusReputation:    '聲望',
  statusDebt:          '（債務中）',
  statusBankruptWarn:  '⚠公會即將破產',

  // Reputation adjective labels (ordered: ≥100 / 60~99 / 30~59 / 10~29 / 0~9 / <0)
  reputationLabels: [
    '傳奇公會',
    '受人敬仰的',
    '受人尊敬的',
    '小有名氣的',
    '默默無聞的',
    '聲名狼藉的',
  ] as const,

  // Commission panel
  commissionPanelTitle:  '委託',
  commissionEmpty:       '目前沒有待接委託',
  commissionSelected:    '已選：',

  // Roster panel
  rosterPanelTitle:      '冒險者名冊',
  rosterEmpty:           '目前沒有可用的冒險者',
  rosterSelected:        '已選：',
  rosterStatusAvailable: '可接任務',
  rosterStatusOnMission: '任務中',
  rosterStatusPending:   '推薦中',

  // Dispatch action
  recommendButton:       '推薦',
  recommendConfirm:      '已向 {adventurer} 推薦 {commission}，等待回應…',
  recommendRefused:      '{adventurer} 婉拒了這份委託：{reason}',

  // Settlement overlay
  settlementTitle:       '公會近況',
  settlementConfirm:     '確認，繼續',
  settlementSuccess:     '[成功]',
  settlementFail:        '[失敗]',
  settlementDeath:       '[失蹤]',
  settlementPyrrhic:     '[險勝]',
  settlementLongOffline: '公會已暫停運作 {days} 天，部分紀錄可能不完整',

  // Toolbar
  toolbarSave:           '存檔',
  toolbarExport:         '匯出備份',
  toolbarImport:         '匯入備份',

  // System state
  loadingText:           '公會載入中…',

  // Guild Hall Scene
  hallLoading:                  '公會載入中…',
  hallQuestBoard:               '委  託  欄',
  hallWaitingArea:              '冒險者等候區',
  hallGuildMaster:              '[公會長]',
  hallDesk:                     '[辦公室]',
  hallBackDoor:                 '[建  設]',
  hallReception:                '[公會櫃臺]',
  hallReceptionCounter:         '[櫃台小姐]',
  hallOffice:                   '[辦公室]',
  hallConstructionTitle:        '[公會建設]',
  hallConstructionPlaceholder:  '尚未實裝QQ',

  // Commission Review Panel
  reviewTitle:    '待審核委託',
  reviewAccept:   '接受並張貼',
  reviewReject:   '拒絕',
  reviewEmpty:    '目前沒有待審核委託',

  // Recommend Panel
  recommendTitle:         '推薦委託',
  recommendMissionsTitle: '已接受的委託',
  recommendRosterTitle:   '在公會的傭兵',
  recommendRosterEmpty:   '目前沒有空閒傭兵',
  recommendMissionsEmpty: '目前沒有待接委託',
  recommendAcceptRate:    '接受率',
  recommendDeathRate:     '死亡率',
  adventurerOnMission:    '[任務中]',
  adventurerDead:         '失蹤',
  adventurerProfessionLabel: '職業：',
  recruitNoCandidates:    '目前沒有招募候選人',
  rosterNoMembers:        '目前沒有冒險者',

  // Guild Master Panel
  guildMasterTitle:           '公會長辦公室',
  guildMasterRosterTitle:     '現有冒險者',
  guildMasterCandidatesTitle: '潛力股',
  guildMasterFireButton:      '開除',
  guildMasterRecruitButton:   '招募',
  guildMasterRecruitCost:     '費用',

  // Info cards
  commissionCardClose:  '[關閉]',
  adventurerCardStatus: '狀態',
  adventurerCardRank:   '階級',

  // Message log
  msgDispatchSent:    '{adventurer} 接受了「{mission}」，出發了！',
  msgMissionSuccess:  '[成功] {adventurer} 完成「{mission}」 {gold}g',
  msgMissionFail:     '[失敗] {adventurer} 失敗「{mission}」 {gold}g',
  msgMissionDeath:    '[失蹤] {adventurer} 失蹤於「{mission}」中',
  msgMissionPyrrhic:  '[慘勝] {adventurer} 完成「{mission}」但犧牲了',

  // Offline Return Event — 「委託匯報」(gdd-offline-return.md §5)
  offlineReturnTitle:       '委託匯報',
  offlineReturnSubtitle:    '職員為您帶回 {count} 筆任務的結算彙報',
  offlineReturnConfirm:     '好的，我了解了',
  offlineReturnBonusActive: '返回獎勵：+{amount}g（限時獎勵）',
  offlineReturnBonusMissed: '錯過了返回獎勵（離線太久了）',
  offlineReturnBonusToast:  '委託獎勵 +{amount}g（歸來獎勵）',

  // Outcome popup messages (outcomeMessage helper)
  outcomeSuccess:  '委託成功！獲得 {gold}g',
  outcomePyrrhic:  '慘勝——冒險者犧牲了，但委託完成。獲得 {gold}g',
  outcomeFailure:  '委託失敗。賠付 {gold}g',
  outcomeDeath:    '冒險者陣亡，委託失敗。賠付 {gold}g',

  // Dispatch rejection messages — single adventurer (onDispatch)
  rejectCapacity:           '委託欄已滿，無法派遣「{name}」',
  rejectWillingnessAll:     '成功率 {rate}% 且死亡風險 {drate}%，風險過高',
  rejectWillingnessDeath:   '死亡風險過高（{drate}%）',
  rejectWillingnessSuccess: '勝算不足（成功率 {rate}%）',
  rejectWillingnessWeak:    '意願不足（成功率 {rate}%，死亡率 {drate}%）',
  rejectRefused:            '「{name}」婉拒了「{mission}」— {reason}',
  rejectLevelMismatch:      '「{name}」無法接受「{mission}」（等級不符）',

  // Dispatch rejection messages — party (onPartyDispatch)
  rejectPartyCapacity:    '委託欄已滿，無法派遣隊伍（{names}）',
  rejectPartyTooLarge:    '隊伍人數超過「{mission}」的上限',
  rejectPartyMemberBusy:  '隊伍中有成員正在執行委託，無法組隊',
  rejectPartyRefused:     '【隊伍】{names} 婉拒了「{mission}」— {reason}',

  // Party dispatch success messages (onPartyDispatch)
  msgPartyDispatchSent:    '【隊伍】{names} 出發前往「{mission}」',
  msgPartySynergy:         '✦ 隊伍加成：{synergies}',
} as const
