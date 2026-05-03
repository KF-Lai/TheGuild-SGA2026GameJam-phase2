// main-menu.ts — Main Menu Screen
// Implements: ASCII-art title, New Game / Load Game / Quit options.
// Style: pure DOM + CSS, monospace, matches guild-hall-scene.ts visual language.

// ---------------------------------------------------------------------------
// Module-level state
// ---------------------------------------------------------------------------

let _menuEl: HTMLElement | null = null
let _keyHandler: ((e: KeyboardEvent) => void) | null = null

// ---------------------------------------------------------------------------
// ASCII art
// ---------------------------------------------------------------------------

const TITLE_ASCII = `
 _____ _   _ _____    ____ _   _ ___ _     ____
|_   _| | | | ____|  / ___| | | |_ _| |   |  _ \\
  | | | |_| |  _|   | |  _| | | || || |   | | | |
  | | |  _  | |___  | |_| | |_| || || |___| |_| |
  |_| |_| |_|_____|  \\____|\\___/|___|_____|____/
`.trim()

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Renders the main menu into `container` and binds keyboard + mouse handlers.
 * @param container  - The #app root element
 * @param callbacks  - onNewGame / onLoadGame / onQuit handlers
 * @param hasSave    - Whether a valid save exists (disables Load if false)
 */
export function showMainMenu(
  container: HTMLElement,
  callbacks: {
    onNewGame: () => void
    onLoadGame: () => void
    onImport: (file: File) => void
  },
  hasSave: boolean,
): void {
  // Clean up any existing menu
  hideMainMenu()

  const el = document.createElement('div')
  el.id = 'main-menu'
  el.style.cssText = [
    'position:fixed',
    'inset:0',
    'display:flex',
    'flex-direction:column',
    'align-items:center',
    'justify-content:center',
    'background:#0d0d0d',
    'color:#c8b98a',
    'font-family:"Courier New",monospace',
    'z-index:9999',
    'user-select:none',
  ].join(';')

  el.innerHTML = buildMenuHTML(hasSave)

  container.appendChild(el)
  _menuEl = el

  // Wire mouse clicks
  el.querySelector<HTMLElement>('#menu-new-game')?.addEventListener('click', () => {
    callbacks.onNewGame()
  })

  const loadBtn = el.querySelector<HTMLElement>('#menu-load-game')
  if (hasSave) {
    loadBtn?.addEventListener('click', () => {
      callbacks.onLoadGame()
    })
  }

  el.querySelector<HTMLElement>('#menu-import')?.addEventListener('click', () => {
    triggerImport(callbacks.onImport)
  })

  // Wire keyboard
  _keyHandler = (e: KeyboardEvent) => {
    switch (e.key.toUpperCase()) {
      case 'N':
        callbacks.onNewGame()
        break
      case 'L':
        if (hasSave) callbacks.onLoadGame()
        break
      case 'I':
        triggerImport(callbacks.onImport)
        break
    }
  }
  window.addEventListener('keydown', _keyHandler)
}

/**
 * Removes the main menu from the DOM and unbinds event listeners.
 */
export function hideMainMenu(): void {
  if (_keyHandler) {
    window.removeEventListener('keydown', _keyHandler)
    _keyHandler = null
  }
  if (_menuEl) {
    _menuEl.remove()
    _menuEl = null
  }
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function buildMenuHTML(hasSave: boolean): string {
  const titleLines = TITLE_ASCII.split('\n')
    .map(line => `<div>${escapeHtml(line)}</div>`)
    .join('')

  const loadStyle = hasSave
    ? 'cursor:pointer;color:#c8b98a;'
    : 'cursor:default;color:#4a4238;'
  const loadHint = hasSave ? '' : ' (無存檔)'

  return `
    <div style="text-align:center;white-space:pre;font-size:clamp(8px,1.1vw,14px);line-height:1.3;margin-bottom:2em;color:#e8d4a0;">
      ${titleLines}
    </div>

    <div style="font-size:clamp(11px,1.4vw,16px);line-height:2.2;text-align:center;width:28ch;">
      <div id="menu-new-game"
           style="cursor:pointer;color:#c8b98a;padding:0.2em 0.5em;border-radius:2px;"
           data-hover>
        [N] 新遊戲
      </div>
      <div id="menu-load-game"
           style="${loadStyle}padding:0.2em 0.5em;border-radius:2px;"
           ${hasSave ? 'data-hover' : ''}>
        [L] 讀取遊戲${loadHint}
      </div>
      <div id="menu-import"
           style="cursor:pointer;color:#c8b98a;padding:0.2em 0.5em;border-radius:2px;"
           data-hover>
        [I] 匯入存檔
      </div>
    </div>

    <div style="margin-top:3em;"></div>

    <div style="margin-top:1.5em;font-size:clamp(11px,1.1vw,15px);color:#4a4238;">
      <a href="https://github.com/KF-Lai/TheGuild-SGA2026gamejam-demo/"
         target="_blank"
         rel="noopener noreferrer"
         style="color:#4a4238;text-decoration:none;"
         onmouseover="this.style.color='#c8b98a'"
         onmouseout="this.style.color='#4a4238'">
        GitHub / The-Guild
      </a>
    </div>
  `
}

function triggerImport(onImport: (file: File) => void): void {
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.json'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) onImport(file)
  }
  input.click()
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}
