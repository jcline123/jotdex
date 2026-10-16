import { describe, expect, it } from 'vitest'
import { resolveNoteLink, resolveWikiTarget, type NoteLinkTarget } from './paths'

const catalog: NoteLinkTarget[] = [
  {
    id: 'a',
    title: 'ConnectWise',
    relativePath: 'Work/ConnectWise/ConnectWise.md',
    folderPath: 'Work/ConnectWise',
  },
  {
    id: 'b',
    title: 'Waiting Client Response on Dental MSP',
    relativePath: 'Work/ConnectWise/Workflows/Waiting Client Response on Dental MSP.md',
    folderPath: 'Work/ConnectWise/Workflows',
  },
  {
    id: 'c',
    title: 'Tickets By Day (90 Days)',
    relativePath: 'Work/BrightGauge/Gauges/Tickets By Day (90 Days).md',
    folderPath: 'Work/BrightGauge/Gauges',
  },
  {
    id: 'd',
    title: 'Service',
    relativePath: 'Work/BrightGauge/Goals/Service.md',
    folderPath: 'Work/BrightGauge/Goals',
  },
]

describe('resolveNoteLink', () => {
  it('resolves an encoded relative note link', () => {
    const hit = resolveNoteLink(
      'Work/ConnectWise/ConnectWise.md',
      'Workflows/Waiting%20Client%20Response%20on%20Dental%20MSP.md',
      catalog,
    )
    expect(hit?.id).toBe('b')
  })

  it('resolves a link whose filename contains parentheses', () => {
    const hit = resolveNoteLink(
      'Work/BrightGauge/Dashboards/Force Sync Executive Summary.md',
      '../Gauges/Tickets%20By%20Day%20(90%20Days).md',
      catalog,
    )
    expect(hit?.id).toBe('c')
  })

  it('ignores external urls', () => {
    expect(resolveNoteLink('Work/ConnectWise/ConnectWise.md', 'https://example.com', catalog)).toBeNull()
  })
})

describe('resolveWikiTarget', () => {
  it('resolves a unique title', () => {
    expect(resolveWikiTarget('Work/BrightGauge/Goals/Goals.md', 'Service', catalog)?.id).toBe('d')
  })

  it('resolves Title|label form', () => {
    expect(resolveWikiTarget('Work/BrightGauge/Goals/Goals.md', 'Service|the scorecard', catalog)?.id).toBe('d')
  })
})
