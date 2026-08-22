import type { Diagnostic } from '@codemirror/lint'
import { normalizeLanguageId } from './codeLanguages'

export type DiagnosticSeverity = 'error' | 'warning' | 'style'

export type CodeDiagnostic = {
  source: string
  severity: DiagnosticSeverity
  message: string
  code?: string
  startLine: number
  startColumn: number
  endLine: number
  endColumn: number
}

export function lineColumnToOffset(text: string, line: number, column: number): number {
  const lines = text.split('\n')
  const idx = Math.max(0, Math.min(line - 1, lines.length - 1))
  let offset = 0
  for (let i = 0; i < idx; i++) offset += lines[i].length + 1
  offset += Math.max(0, column - 1)
  return Math.min(offset, text.length)
}

export function toCodeMirrorDiagnostics(text: string, items: CodeDiagnostic[]): Diagnostic[] {
  return items.map((d) => {
    const from = lineColumnToOffset(text, d.startLine, d.startColumn)
    const to = lineColumnToOffset(text, d.endLine, d.endColumn)
    return {
      from: Math.min(from, to),
      to: Math.max(from, to, from + 1),
      severity: d.severity === 'error' ? 'error' : d.severity === 'warning' ? 'warning' : 'info',
      message: d.message,
      source: d.source,
    }
  })
}

/** Client-side JSON syntax check (does not block saving). */
export function validateJsonSyntax(text: string): CodeDiagnostic[] {
  if (!text.trim()) return []
  try {
    JSON.parse(text)
    return []
  } catch (err) {
    const msg = err instanceof Error ? err.message : 'Invalid JSON'
    const pos = parseJsonErrorPosition(msg, text)
    return [
      {
        source: 'json-syntax',
        severity: 'error',
        message: msg,
        startLine: pos.line,
        startColumn: pos.column,
        endLine: pos.line,
        endColumn: pos.column + 1,
      },
    ]
  }
}

function parseJsonErrorPosition(message: string, text: string): { line: number; column: number } {
  const atPos = message.match(/position\s+(\d+)/i)
  if (atPos) {
    const offset = Number(atPos[1])
    return offsetToLineColumn(text, offset)
  }
  const lineCol = message.match(/line\s+(\d+)\s+column\s+(\d+)/i)
  if (lineCol) {
    return { line: Number(lineCol[1]), column: Number(lineCol[2]) }
  }
  return { line: 1, column: 1 }
}

function offsetToLineColumn(text: string, offset: number): { line: number; column: number } {
  const safe = Math.max(0, Math.min(offset, text.length))
  let line = 1
  let column = 1
  for (let i = 0; i < safe; i++) {
    if (text[i] === '\n') {
      line++
      column = 1
    } else {
      column++
    }
  }
  return { line, column }
}

/** Non-destructive style hints for the advanced editor. */
export function validateCodeStyle(text: string): CodeDiagnostic[] {
  const out: CodeDiagnostic[] = []
  const lines = text.split('\n')
  let hasTab = false
  let hasSpaceIndent = false

  lines.forEach((line, i) => {
    const lineNo = i + 1
    if (/\s+$/.test(line) && line.trim().length > 0) {
      out.push({
        source: 'style',
        severity: 'style',
        message: 'Trailing whitespace',
        startLine: lineNo,
        startColumn: line.trimEnd().length + 1,
        endLine: lineNo,
        endColumn: line.length + 1,
      })
    }
    if (/^\t/.test(line)) hasTab = true
    if (/^ {2,}/.test(line)) hasSpaceIndent = true
    if (line.length > 200) {
      out.push({
        source: 'style',
        severity: 'style',
        message: 'Line exceeds 200 characters',
        startLine: lineNo,
        startColumn: 201,
        endLine: lineNo,
        endColumn: line.length + 1,
      })
    }
  })

  if (hasTab && hasSpaceIndent) {
    out.unshift({
      source: 'style',
      severity: 'style',
      message: 'Mixed tabs and spaces for indentation',
      startLine: 1,
      startColumn: 1,
      endLine: 1,
      endColumn: 2,
    })
  }

  // Do not warn about a trailing newline: Markdown fences already place the closing
  // ``` on its own line, and forcing content to end with \n polluted vault code boxes.

  return out
}

export type PowerShellDiagnosticsResponse = {
  label?: string
  scriptAnalyzerAvailable?: boolean
  scriptAnalyzerStatus?: string
  diagnostics?: Array<{
    source: string
    severity: string
    message: string
    code?: string
    startLine: number
    startColumn: number
    endLine: number
    endColumn: number
  }>
  error?: string
}

export async function fetchPowerShellDiagnostics(
  code: string,
  signal: AbortSignal,
): Promise<{ label: string; diagnostics: CodeDiagnostic[]; scriptAnalyzerStatus?: string }> {
  const res = await fetch('/api/code-diagnostics/powershell', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
    signal,
  })
  const data = (await res.json()) as PowerShellDiagnosticsResponse
  if (!res.ok) {
    throw new Error(data.error ?? 'PowerShell syntax check failed')
  }
  const diagnostics: CodeDiagnostic[] = (data.diagnostics ?? []).map((d) => ({
    source: d.source,
    severity: normalizeSeverity(d.severity),
    message: d.message,
    code: d.code,
    startLine: d.startLine,
    startColumn: d.startColumn,
    endLine: d.endLine,
    endColumn: d.endColumn,
  }))
  return {
    label: data.label ?? 'PowerShell syntax',
    diagnostics,
    scriptAnalyzerStatus: data.scriptAnalyzerStatus,
  }
}

function normalizeSeverity(s: string): DiagnosticSeverity {
  if (s === 'warning') return 'warning'
  if (s === 'style') return 'style'
  return 'error'
}

export function diagnosticsForLanguage(language: string, text: string): CodeDiagnostic[] {
  const id = normalizeLanguageId(language)
  if (id === 'json') return [...validateJsonSyntax(text), ...validateCodeStyle(text)]
  return validateCodeStyle(text)
}

export async function asyncDiagnosticsForLanguage(
  language: string,
  text: string,
  signal: AbortSignal,
): Promise<{ diagnostics: CodeDiagnostic[]; hint?: string }> {
  const id = normalizeLanguageId(language)
  const base = diagnosticsForLanguage(language, text)
  if (id === 'powershell' && text.trim()) {
    try {
      const { diagnostics, scriptAnalyzerStatus } = await fetchPowerShellDiagnostics(text, signal)
      return { diagnostics: [...diagnostics, ...base], hint: scriptAnalyzerStatus }
    } catch (err) {
      if ((err as Error).name === 'AbortError') throw err
      return {
        diagnostics: [
          ...base,
          {
            source: 'powershell-syntax',
            severity: 'warning',
            message: err instanceof Error ? err.message : 'Could not check PowerShell syntax',
            startLine: 1,
            startColumn: 1,
            endLine: 1,
            endColumn: 2,
          },
        ],
      }
    }
  }
  return { diagnostics: base }
}
