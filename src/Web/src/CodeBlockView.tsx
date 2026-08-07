import { NodeViewContent, NodeViewWrapper } from '@tiptap/react'
import type { NodeViewProps } from '@tiptap/react'
import { useState } from 'react'

export const CODE_LANGUAGES = [
  { id: 'plaintext', label: 'Plain text' },
  { id: 'powershell', label: 'PowerShell' },
  { id: 'bash', label: 'Bash / shell' },
  { id: 'cmd', label: 'CMD' },
  { id: 'csharp', label: 'C#' },
  { id: 'javascript', label: 'JavaScript' },
  { id: 'typescript', label: 'TypeScript' },
  { id: 'json', label: 'JSON' },
  { id: 'sql', label: 'SQL' },
  { id: 'python', label: 'Python' },
  { id: 'yaml', label: 'YAML' },
  { id: 'xml', label: 'XML / HTML' },
] as const

export function CodeBlockView({ node, updateAttributes, editor }: NodeViewProps) {
  const [copied, setCopied] = useState(false)
  const language = (node.attrs.language as string) || 'plaintext'

  const copy = async () => {
    const text = node.textContent
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1400)
    } catch {
      // Fallback for older contexts
      const ta = document.createElement('textarea')
      ta.value = text
      ta.style.position = 'fixed'
      ta.style.left = '-9999px'
      document.body.appendChild(ta)
      ta.select()
      document.execCommand('copy')
      document.body.removeChild(ta)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1400)
    }
  }

  return (
    <NodeViewWrapper className="code-block-box" data-language={language}>
      <div className="code-block-chrome" contentEditable={false}>
        <label className="code-lang">
          <span className="sr-only">Language</span>
          <select
            value={CODE_LANGUAGES.some((l) => l.id === language) ? language : 'plaintext'}
            disabled={!editor.isEditable}
            onChange={(e) => updateAttributes({ language: e.target.value })}
            aria-label="Code language"
          >
            {CODE_LANGUAGES.map((l) => (
              <option key={l.id} value={l.id}>
                {l.label}
              </option>
            ))}
            {!CODE_LANGUAGES.some((l) => l.id === language) && language !== 'plaintext' && (
              <option value={language}>{language}</option>
            )}
          </select>
        </label>
        <button type="button" className="code-copy-btn" onClick={() => void copy()} title="Copy code">
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      <pre className="code-block-pre" spellCheck={false}>
        {/* TipTap NodeViewContent types omit "code"; runtime supports it */}
        <NodeViewContent as={'code' as 'div'} className={`hljs language-${language}`} />
      </pre>
    </NodeViewWrapper>
  )
}
