import { describe, expect, it } from 'vitest'
import { matchSlashTrigger } from './slashMenuPlugin'

describe('matchSlashTrigger', () => {
  it('matches a fresh slash at paragraph start', () => {
    expect(matchSlashTrigger('/')).toEqual({ query: '', triggerOffset: 0 })
    expect(matchSlashTrigger('/hea')).toEqual({ query: 'hea', triggerOffset: 0 })
  })

  it('matches after whitespace', () => {
    expect(matchSlashTrigger('Hello /')).toEqual({ query: '', triggerOffset: 6 })
    expect(matchSlashTrigger('Hello /code')).toEqual({ query: 'code', triggerOffset: 6 })
  })

  it('does not match mid-word or URL-like paths', () => {
    expect(matchSlashTrigger('https://example.com')).toBeNull()
    expect(matchSlashTrigger('a/b')).toBeNull()
    expect(matchSlashTrigger('10/12/2026')).toBeNull()
  })

  it('does not match when spaces follow the slash query (incidental prose)', () => {
    expect(matchSlashTrigger('see /foo and more')).toBeNull()
    expect(matchSlashTrigger('/foo bar')).toBeNull()
  })
})
