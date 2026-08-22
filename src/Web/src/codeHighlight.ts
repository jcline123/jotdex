import { common, createLowlight } from 'lowlight'
import powershell from 'highlight.js/lib/languages/powershell'
import dos from 'highlight.js/lib/languages/dos'

export const codeLowlight = createLowlight(common)
codeLowlight.register('powershell', powershell)
codeLowlight.register('ps1', powershell)
codeLowlight.register('pwsh', powershell)
codeLowlight.register('cmd', dos)
codeLowlight.register('bat', dos)
