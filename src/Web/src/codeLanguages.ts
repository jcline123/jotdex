import type { Extension } from '@codemirror/state'
import { StreamLanguage } from '@codemirror/language'

/** Normalize fence language tags to a canonical id used by Jotdex UI and CodeMirror. */
const ALIASES: Record<string, string> = {
  text: 'plaintext',
  txt: 'plaintext',
  ps1: 'powershell',
  pwsh: 'powershell',
  sh: 'bash',
  shell: 'bash',
  bat: 'cmd',
  dos: 'cmd',
  js: 'javascript',
  ts: 'typescript',
  py: 'python',
  yml: 'yaml',
  html: 'xml',
  cs: 'csharp',
}

export function normalizeLanguageId(raw: string | null | undefined): string {
  const id = (raw || 'plaintext').trim().toLowerCase()
  return ALIASES[id] ?? id
}

type LanguageLoader = () => Promise<Extension>

const LOADERS: Record<string, LanguageLoader> = {
  javascript: async () => (await import('@codemirror/lang-javascript')).javascript(),
  typescript: async () => (await import('@codemirror/lang-javascript')).javascript({ typescript: true }),
  json: async () => (await import('@codemirror/lang-json')).json(),
  python: async () => (await import('@codemirror/lang-python')).python(),
  sql: async () => (await import('@codemirror/lang-sql')).sql(),
  yaml: async () => (await import('@codemirror/lang-yaml')).yaml(),
  xml: async () => (await import('@codemirror/lang-xml')).xml(),
  bash: async () => {
    const { shell } = await import('@codemirror/legacy-modes/mode/shell')
    return StreamLanguage.define(shell)
  },
  cmd: async () => {
    const { shell } = await import('@codemirror/legacy-modes/mode/shell')
    return StreamLanguage.define(shell)
  },
  powershell: async () => {
    const { powerShell } = await import('@codemirror/legacy-modes/mode/powershell')
    return StreamLanguage.define(powerShell)
  },
  csharp: async () => {
    const { csharp } = await import('@codemirror/legacy-modes/mode/clike')
    return StreamLanguage.define(csharp)
  },
}

const cache = new Map<string, Promise<Extension>>()

/** Lazy language support; returns [] for plaintext / unknown (safe fallback). */
export function loadCodeMirrorLanguage(raw: string): Promise<Extension> {
  const id = normalizeLanguageId(raw)
  if (id === 'plaintext') return Promise.resolve([])

  const loader = LOADERS[id]
  if (!loader) return Promise.resolve([])

  let pending = cache.get(id)
  if (!pending) {
    pending = loader().catch(() => [] as Extension)
    cache.set(id, pending)
  }
  return pending
}

export function languageLabel(id: string): string {
  const n = normalizeLanguageId(id)
  const labels: Record<string, string> = {
    plaintext: 'Plain text',
    powershell: 'PowerShell',
    bash: 'Bash / shell',
    cmd: 'CMD',
    csharp: 'C#',
    javascript: 'JavaScript',
    typescript: 'TypeScript',
    json: 'JSON',
    sql: 'SQL',
    python: 'Python',
    yaml: 'YAML',
    xml: 'XML / HTML',
  }
  return labels[n] ?? n
}
