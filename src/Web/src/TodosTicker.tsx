import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'

type Props = {
  titles: string[]
}

/** Pixels per second — slow enough to read, steady for any list length. */
const SCROLL_PX_PER_SEC = 28
const MIN_GAP_PX = 32
const MIN_DURATION_SEC = 10

/**
 * Seamless vertical marquee for the collapsed Todos rail.
 * Two identical cycles; each cycle is padded to at least the viewport height so
 * the same title never appears twice on screen, and the loop uses an exact
 * pixel distance (no % seam flash).
 */
export function TodosTicker({ titles }: Props) {
  const viewportRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<HTMLDivElement>(null)
  const [cyclePx, setCyclePx] = useState(0)
  const [padPx, setPadPx] = useState(MIN_GAP_PX)
  const titlesKey = titles.join('\0')

  useLayoutEffect(() => {
    const viewport = viewportRef.current
    const measure = measureRef.current
    if (!viewport || !measure || titles.length === 0) {
      setCyclePx(0)
      return
    }

    const update = () => {
      const vpH = viewport.clientHeight
      const itemsH = measure.scrollHeight
      if (vpH <= 0 || itemsH <= 0) {
        setCyclePx(0)
        return
      }
      // Keep one full cycle off-screen: pad so cycle height >= viewport.
      const pad = itemsH >= vpH ? MIN_GAP_PX : Math.max(MIN_GAP_PX, vpH - itemsH)
      const cycle = itemsH + pad
      setPadPx((prev) => (prev === pad ? prev : pad))
      setCyclePx((prev) => (prev === cycle ? prev : cycle))
    }

    update()
    const ro = new ResizeObserver(update)
    ro.observe(viewport)
    ro.observe(measure)
    return () => ro.disconnect()
  }, [titles.length, titlesKey])

  if (titles.length === 0) return null

  const durationSec =
    cyclePx > 0 ? Math.max(MIN_DURATION_SEC, cyclePx / SCROLL_PX_PER_SEC) : MIN_DURATION_SEC
  const ready = cyclePx > 0

  const trackStyle = {
    ['--ticker-cycle']: `${cyclePx}px`,
    animationDuration: `${durationSec}s`,
  } as CSSProperties

  return (
    <div className="todos-ticker" ref={viewportRef} aria-hidden>
      <div className="todos-ticker-measure" ref={measureRef}>
        {titles.map((title, i) => (
          <span key={`m-${i}`} className="todos-ticker-item">
            {title}
          </span>
        ))}
      </div>

      <div className={`todos-ticker-track${ready ? ' is-ready' : ''}`} style={trackStyle}>
        {[0, 1].map((copy) => (
          <div key={copy} className="todos-ticker-group" style={{ paddingBottom: padPx }}>
            {titles.map((title, i) => (
              <span key={`${copy}-${i}`} className="todos-ticker-item">
                {title}
              </span>
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}
