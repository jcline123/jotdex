export const IMAGE_WIDTH_MIN_PCT = 20
export const IMAGE_WIDTH_MAX_PCT = 100
export const IMAGE_WIDTH_STEP_PCT = 5

export function clampImageWidthPercent(n: number): number {
  if (!Number.isFinite(n)) return IMAGE_WIDTH_MAX_PCT
  return Math.min(IMAGE_WIDTH_MAX_PCT, Math.max(IMAGE_WIDTH_MIN_PCT, Math.round(n)))
}

/** Display width as a percent of the editor column. */
export function currentImageWidthPercent(
  stored: string | null | undefined,
  displayedPx: number,
  containerPx: number,
): number {
  const raw = (stored ?? '').trim()
  if (raw.endsWith('%')) {
    return clampImageWidthPercent(Number.parseFloat(raw.slice(0, -1)))
  }
  const container = containerPx > 0 ? containerPx : displayedPx
  if (raw && /^\d+(\.\d+)?$/.test(raw) && container > 0) {
    const px = Number.parseFloat(raw)
    if (Number.isFinite(px) && px > 0) return clampImageWidthPercent((px / container) * 100)
  }
  if (container > 0 && displayedPx > 0) {
    return clampImageWidthPercent((displayedPx / container) * 100)
  }
  return IMAGE_WIDTH_MAX_PCT
}

/** 100% means default (no width attr) unless other figure attrs keep it a figure. */
export function storedWidthFromPercent(pct: number): string | null {
  const n = clampImageWidthPercent(pct)
  if (n >= IMAGE_WIDTH_MAX_PCT) return null
  return `${n}%`
}

export function nudgeImageWidthPercent(current: number, dir: -1 | 1): number {
  return clampImageWidthPercent(current + dir * IMAGE_WIDTH_STEP_PCT)
}
