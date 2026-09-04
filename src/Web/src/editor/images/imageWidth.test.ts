import { describe, expect, it } from 'vitest'
import {
  clampImageWidthPercent,
  currentImageWidthPercent,
  nudgeImageWidthPercent,
  storedWidthFromPercent,
} from './imageWidth'

describe('imageWidth', () => {
  it('clamps to 20–100', () => {
    expect(clampImageWidthPercent(5)).toBe(20)
    expect(clampImageWidthPercent(165)).toBe(100)
    expect(clampImageWidthPercent(47.4)).toBe(47)
  })

  it('stores percent and omits 100', () => {
    expect(storedWidthFromPercent(65)).toBe('65%')
    expect(storedWidthFromPercent(100)).toBeNull()
    expect(storedWidthFromPercent(12)).toBe('20%')
  })

  it('reads stored percent, pixels, or displayed size', () => {
    expect(currentImageWidthPercent('65%', 200, 400)).toBe(65)
    expect(currentImageWidthPercent('200', 200, 400)).toBe(50)
    expect(currentImageWidthPercent(null, 200, 400)).toBe(50)
  })

  it('nudges by 5 percent', () => {
    expect(nudgeImageWidthPercent(65, 1)).toBe(70)
    expect(nudgeImageWidthPercent(22, -1)).toBe(20)
  })
})
