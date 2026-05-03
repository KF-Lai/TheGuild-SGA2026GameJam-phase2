// cheat-menu.ts — Debug / Cheat Panel
// Self-contained UI component. No game logic — all actions via CheatCallbacks.

export interface CheatCallbacks {
  onAddGold100:          () => void
  onAddGold1000:         () => void
  onRemoveGold100:       () => void
  onRemoveGold1000:      () => void
  onRefreshAdventurers:  () => void
  onRefreshMissions:     () => void
  onToggleForceAccept:   (enabled: boolean) => void
  onAdvanceTime30m:      () => void
  onAdvanceTime1h:       () => void
  onAdvanceTime5h:       () => void
  onIncreaseWorldDanger: () => void
  onDecreaseWorldDanger: () => void
  onCompleteAllMissions: () => void
  onFailAllMissions:     () => void
  onIncreaseReputation:  () => void
  onDecreaseReputation:  () => void
}

/**
 * Returns a wrapper element containing the toggle button and the cheat panel.
 * Append this to the toolbar. The panel floats above the toolbar when open.
 * @param callbacks - cheat action callbacks
 * @param initialOpen - whether the panel should start open (for re-render persistence)
 * @param onToggle - called with the new open state whenever the panel is toggled
 */
export function renderCheatButton(
  callbacks: CheatCallbacks,
  initialOpen = false,
  onToggle?: (open: boolean) => void,
): HTMLElement {
  let panelOpen = initialOpen
  let forceAcceptEnabled = false

  // Wrapper holds both the toggle button and the panel
  const wrapper = document.createElement('div')
  wrapper.style.cssText = 'position:relative;display:inline-block'

  // Panel (created once, toggled visible)
  const panel = document.createElement('div')
  panel.style.cssText = [
    panelOpen ? 'display:block' : 'display:none',
    'position:absolute',
    'bottom:calc(100% + 4px)',
    'left:0',
    'background:#1a1a1a',
    'border:1px solid #ff6600',
    'padding:8px',
    'z-index:200',
    'width:260px',
  ].join(';')

  // Grid container for the 12 buttons (2 columns)
  const grid = document.createElement('div')
  grid.style.cssText = 'display:grid;grid-template-columns:1fr 1fr;gap:4px'

  // Helper: build a standard cheat button
  function makeBtn(label: string, onClick: () => void): HTMLButtonElement {
    const btn = document.createElement('button')
    btn.style.cssText = [
      'padding:4px 6px',
      'background:transparent',
      'color:#c8b98a',
      'border:1px solid #555',
      'font-family:\'Courier New\',monospace',
      'font-size:12px',
      'cursor:pointer',
      'text-align:left',
    ].join(';')
    btn.textContent = label
    btn.addEventListener('click', onClick)
    return btn
  }

  // Button 7 (toggle) needs a reference so we can update its label
  const forceAcceptBtn = makeBtn('100%接受委託 [關]', () => {
    forceAcceptEnabled = !forceAcceptEnabled
    forceAcceptBtn.textContent = `100%接受委託 [${forceAcceptEnabled ? '開' : '關'}]`
    callbacks.onToggleForceAccept(forceAcceptEnabled)
  })

  const buttons: HTMLButtonElement[] = [
    makeBtn('+100金',    callbacks.onAddGold100),
    makeBtn('+1000金',   callbacks.onAddGold1000),
    makeBtn('-100金',    callbacks.onRemoveGold100),
    makeBtn('-1000金',   callbacks.onRemoveGold1000),
    makeBtn('刷新冒險者',   callbacks.onRefreshAdventurers),
    makeBtn('刷新委託庫',   callbacks.onRefreshMissions),
    forceAcceptBtn,
    makeBtn('推進30分鐘',  callbacks.onAdvanceTime30m),
    makeBtn('推進1小時',   callbacks.onAdvanceTime1h),
    makeBtn('推進5小時',   callbacks.onAdvanceTime5h),
    makeBtn('世界危險度+1', callbacks.onIncreaseWorldDanger),
    makeBtn('世界危險度-1', callbacks.onDecreaseWorldDanger),
    makeBtn('完成所有任務',  callbacks.onCompleteAllMissions),
    makeBtn('所有任務失敗',  callbacks.onFailAllMissions),
    makeBtn('公會聲望+1階', callbacks.onIncreaseReputation),
    makeBtn('公會聲望-1階', callbacks.onDecreaseReputation),
  ]

  buttons.forEach(b => grid.appendChild(b))
  panel.appendChild(grid)

  // Toggle button
  const toggleBtn = document.createElement('button')
  toggleBtn.style.cssText = [
    'padding:4px 12px',
    'background:transparent',
    'color:#ff6600',
    'border:1px solid #ff6600',
    'font-family:\'Courier New\',monospace',
    'font-size:13px',
    'cursor:pointer',
  ].join(';')
  toggleBtn.textContent = '[金手指]'
  function closePanel() {
    panelOpen = false
    panel.style.display = 'none'
    onToggle?.(false)
  }

  toggleBtn.addEventListener('click', (e) => {
    e.stopPropagation()
    panelOpen = !panelOpen
    panel.style.display = panelOpen ? 'block' : 'none'
    onToggle?.(panelOpen)

    if (panelOpen) {
      setTimeout(() => {
        document.addEventListener('click', function handler(ev) {
          if (!wrapper.contains(ev.target as Node)) {
            closePanel()
            document.removeEventListener('click', handler)
          }
        })
      }, 0)
    }
  })

  wrapper.appendChild(toggleBtn)
  wrapper.appendChild(panel)

  return wrapper
}
