import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const srcRoot = join(process.cwd(), 'src')
const allowed = new Set([
  'editor/operations/contentInsertion.ts',
  'editor/markdown/officialSpike.test.ts',
])

const STRING_CALL = /\.(setContent|insertContent|insertContentAt)\(\s*(['"`])/g

function walk(dir: string, acc: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    const st = statSync(full)
    if (st.isDirectory()) {
      if (name === 'node_modules') continue
      walk(full, acc)
    } else if (/\.(ts|tsx)$/.test(name) && !name.endsWith('.test.ts') && !name.endsWith('.test.tsx')) {
      acc.push(full)
    }
  }
  return acc
}

describe('content insertion audit', () => {
  it('forbids raw string setContent/insertContent outside the helper module', () => {
    const hits: string[] = []
    for (const file of walk(srcRoot)) {
      const rel = relative(srcRoot, file).replace(/\\/g, '/')
      if (allowed.has(rel)) continue
      const text = readFileSync(file, 'utf8')
      STRING_CALL.lastIndex = 0
      if (STRING_CALL.test(text)) hits.push(rel)
    }
    expect(hits).toEqual([])
  })
})
