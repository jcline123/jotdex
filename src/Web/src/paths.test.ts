import { describe, expect, it } from 'vitest'
import { resolveNoteLink, resolveWikiTarget, type NoteLinkTarget } from './paths'

const catalog: NoteLinkTarget[] = [
  {
    id: 'a',
    title: 'Index',
    relativePath: 'Projects/Runbooks/Index.md',
    folderPath: 'Projects/Runbooks',
  },
  {
    id: 'b',
    title: 'Vendor follow-up',
    relativePath: 'Projects/Runbooks/Workflows/Vendor follow-up.md',
    folderPath: 'Projects/Runbooks/Workflows',
  },
  {
    id: 'c',
    title: 'Tickets by day (90 days)',
    relativePath: 'Projects/Dashboards/Gauges/Tickets by day (90 days).md',
    folderPath: 'Projects/Dashboards/Gauges',
  },
  {
    id: 'd',
    title: 'Service',
    relativePath: 'Projects/Dashboards/Goals/Service.md',
    folderPath: 'Projects/Dashboards/Goals',
  },
]

describe('resolveNoteLink', () => {
  it('resolves an encoded relative note link', () => {
    const hit = resolveNoteLink(
      'Projects/Runbooks/Index.md',
      'Workflows/Vendor%20follow-up.md',
      catalog,
    )
    expect(hit?.id).toBe('b')
  })

  it('resolves a link whose filename contains parentheses', () => {
    const hit = resolveNoteLink(
      'Projects/Dashboards/Boards/Executive summary.md',
      '../Gauges/Tickets%20by%20day%20(90%20days).md',
      catalog,
    )
    expect(hit?.id).toBe('c')
  })

  it('ignores external urls', () => {
    expect(resolveNoteLink('Projects/Runbooks/Index.md', 'https://example.com', catalog)).toBeNull()
  })
})

describe('resolveWikiTarget', () => {
  it('resolves a unique title', () => {
    expect(resolveWikiTarget('Projects/Dashboards/Goals/Goals.md', 'Service', catalog)?.id).toBe('d')
  })

  it('resolves Title|label form', () => {
    expect(resolveWikiTarget('Projects/Dashboards/Goals/Goals.md', 'Service|the scorecard', catalog)?.id).toBe('d')
  })
})
