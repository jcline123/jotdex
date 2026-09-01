import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const webRequire = createRequire(join(fileURLToPath(new URL('.', import.meta.url)), '../../src/Web/package.json'))
const { JSDOM } = webRequire('jsdom') as { JSDOM: typeof import('jsdom').JSDOM }

const dom = new JSDOM('<!doctype html><html><body></body></html>')
;(globalThis as { window?: unknown; document?: unknown; Node?: unknown }).window = dom.window
;(globalThis as { window?: unknown; document?: unknown; Node?: unknown }).document = dom.window.document
;(globalThis as { window?: unknown; document?: unknown; Node?: unknown }).Node = dom.window.Node

import { createHash } from 'node:crypto'
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs'
import { relative, resolve } from 'node:path'
import { createOfficialMarkdownCodec } from '../../src/Web/src/editor/markdown/OfficialMarkdownCodec'

type Classification = 'ok' | 'source-only' | 'parse-error'

type FileReport = {
  relativePath: string
  sha256: string
  bytes: number
  officialOk: boolean
  classification: Classification
  sourceOnlyReason?: string
  diagnostics: string[]
}

function walkMd(root: string, acc: string[] = []): string[] {
  for (const name of readdirSync(root, { withFileTypes: true })) {
    const full = join(root, name.name)
    if (name.isDirectory()) {
      if (name.name === '.git') continue
      walkMd(full, acc)
    } else if (name.name.toLowerCase().endsWith('.md')) acc.push(full)
  }
  return acc
}

function sha256(buf: Buffer | string): string {
  return createHash('sha256').update(buf).digest('hex')
}

function splitBody(raw: string): string {
  if (!raw.startsWith('---')) return raw
  const end = raw.indexOf('\n---', 3)
  if (end < 0) return raw
  return raw.slice(end + 4).replace(/^\r?\n/, '')
}

function parseArgs(argv: string[]) {
  const args = argv.slice(2)
  const cmd = args[0] ?? 'audit'
  const get = (name: string) => {
    const i = args.indexOf(name)
    return i >= 0 ? args[i + 1] : undefined
  }
  return {
    cmd,
    vault: get('--vault'),
    output: get('--output'),
    staged: get('--staged'),
    manifest: get('--manifest'),
    backup: get('--backup'),
    apply: args.includes('--apply'),
  }
}

function assertNotLiveVault(vault: string) {
  if (resolve(vault) === resolve('C:\\JotdexVault')) {
    throw new Error('Refusing to write C:\\JotdexVault. Use the backup/staged copy.')
  }
}

function copyDir(src: string, dest: string) {
  mkdirSync(dest, { recursive: true })
  for (const name of readdirSync(src, { withFileTypes: true })) {
    const from = join(src, name.name)
    const to = join(dest, name.name)
    if (name.isDirectory()) copyDir(from, to)
    else copyFileSync(from, to)
  }
}

function audit(vault: string, output: string) {
  mkdirSync(output, { recursive: true })
  const official = createOfficialMarkdownCodec()
  const reports: FileReport[] = []
  for (const file of walkMd(vault)) {
    const raw = readFileSync(file, 'utf8')
    const body = splitBody(raw)
    const o = official.parse(body)
    const inspection = official.inspect(body)
    let classification: Classification = 'ok'
    if (!o.ok && o.forcedSourceReason) classification = 'source-only'
    else if (!o.ok) classification = 'parse-error'
    if (inspection.sourceOnly) classification = 'source-only'
    reports.push({
      relativePath: relative(vault, file),
      sha256: sha256(raw),
      bytes: Buffer.byteLength(raw),
      officialOk: o.ok,
      classification,
      sourceOnlyReason: o.forcedSourceReason ?? inspection.reason,
      diagnostics: [...o.diagnostics, ...inspection.diagnostics].map((d) => d.code),
    })
  }
  official.destroy?.()
  const counts = reports.reduce(
    (acc, r) => {
      acc[r.classification] = (acc[r.classification] ?? 0) + 1
      return acc
    },
    {} as Record<string, number>,
  )
  const manifest = { vault, generated: new Date().toISOString(), files: reports.length, counts, reports }
  writeFileSync(join(output, 'manifest.json'), JSON.stringify(manifest, null, 2), 'utf8')
  writeFileSync(
    join(output, 'report.md'),
    [
      '# Official Markdown audit',
      '',
      `Vault: ${vault}`,
      `Files: ${reports.length}`,
      `Counts: ${JSON.stringify(counts)}`,
      '',
      '## Source-only / errors',
      ...reports
        .filter((r) => r.classification !== 'ok')
        .slice(0, 120)
        .map((r) => `- ${r.relativePath}: ${r.classification} ${r.sourceOnlyReason ?? r.diagnostics.join(',')}`),
    ].join('\n'),
    'utf8',
  )
  console.log(`Wrote ${join(output, 'manifest.json')} (${reports.length} notes) ${JSON.stringify(counts)}`)
}

function stage(vault: string, output: string) {
  assertNotLiveVault(output)
  mkdirSync(output, { recursive: true })
  const files = walkMd(vault)
  for (const file of files) {
    const dest = join(output, relative(vault, file))
    mkdirSync(dirname(dest), { recursive: true })
    copyFileSync(file, dest)
    const assets = file.replace(/\.md$/i, '.assets')
    if (existsSync(assets) && statSync(assets).isDirectory()) copyDir(assets, dest.replace(/\.md$/i, '.assets'))
  }
  writeFileSync(join(output, '.migration-staged'), new Date().toISOString(), 'utf8')
  console.log(`Staged copy at ${output} (${files.length} notes). Source unchanged.`)
}

function verify(vault: string, manifestPath: string) {
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8')) as { reports: FileReport[] }
  let ok = 0
  let bad = 0
  for (const r of manifest.reports) {
    const file = join(vault, r.relativePath)
    if (!existsSync(file) || sha256(readFileSync(file)) !== r.sha256) {
      console.error(`drift ${r.relativePath}`)
      bad++
    } else ok++
  }
  console.log(`verify ok=${ok} bad=${bad}`)
  if (bad) process.exitCode = 1
}

function rollback(vault: string, backup: string) {
  assertNotLiveVault(vault)
  copyDir(backup, vault)
  console.log(`Restored ${vault} from ${backup}`)
}

function apply(vault: string, staged: string, manifestPath: string) {
  assertNotLiveVault(vault)
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8')) as { reports: FileReport[] }
  for (const r of manifest.reports) {
    const live = join(vault, r.relativePath)
    if (!existsSync(live) || sha256(readFileSync(live)) !== r.sha256) {
      throw new Error(`abort: hash drift or missing ${r.relativePath}`)
    }
  }
  copyDir(staged, vault)
  console.log(`Applied staged vault onto ${vault}`)
}

const opts = parseArgs(process.argv)
if (!opts.vault) {
  console.error('Usage: npm run markdown:migrate -- audit|stage|verify|rollback|apply --vault PATH')
  process.exit(1)
}
const vault = resolve(opts.vault)
if (opts.cmd === 'audit') audit(vault, resolve(opts.output ?? 'C:\\JotdexMigration\\audit'))
else if (opts.cmd === 'stage') stage(vault, resolve(opts.output ?? 'C:\\JotdexMigration\\staged'))
else if (opts.cmd === 'verify') verify(vault, resolve(opts.manifest ?? 'C:\\JotdexMigration\\audit\\manifest.json'))
else if (opts.cmd === 'rollback') rollback(vault, resolve(opts.backup ?? 'C:\\JotdexMigration\\backup'))
else if (opts.cmd === 'apply') {
  if (!opts.apply) throw new Error('apply requires --apply')
  apply(vault, resolve(opts.staged ?? 'C:\\JotdexMigration\\staged'), resolve(opts.manifest ?? ''))
} else console.log('commands: audit stage verify rollback apply')
