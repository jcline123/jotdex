import { useLayoutEffect, useRef, useState } from 'react'

type Props = {
  titles: string[]
}

/** Pixels per second — slow enough to read, steady for any list length. */
const SCROLL_PX_PER_SEC = 28
/** Minimum blank space between the last title and the next cycle's first title. */
const MIN_GAP_PX = 40
/** Three copies so a 1px measure error never leaves empty rail. */
const COPIES = 3

/**
 * Seamless vertical marquee for the collapsed Todos rail.
 *
 * CSS `@keyframes` loops jump from `to` → `from` in one frame. On Windows Chrome
 * with fractional devicePixelRatio that jump shows as a gap/flash even when the
 * pixel distance is “correct.” Drive the transform with rAF + modulo instead so
 * the motion never resets, and use the first group's real `offsetHeight` as the
 * cycle (not a separately computed items+pad estimate).
 */
export function TodosTicker({ titles }: Props) {
  const viewportRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<HTMLDivElement>(null)
  const trackRef = useRef<HTMLDivElement>(null)
  const groupRef = useRef<HTMLDivElement>(null)
  const [padPx, setPadPx] = useState(MIN_GAP_PX)
  const [ready, setReady] = useState(false)
  const cyclePxRef = useRef(0)
  const offsetRef = useRef(0)
  const rafRef = useRef(0)
  const lastTsRef = useRef(0)
  const titlesKey = titles.join('\0')

  // Pad each copy so one cycle is at least as tall as the viewport (no duplicate
  // titles on screen at once when the list is short).
  useLayoutEffect(() => {
    const viewport = viewportRef.current
    const measure = measureRef.current
    if (!viewport || !measure || titles.length === 0) {
      setPadPx(MIN_GAP_PX)
      setReady(false)
      cyclePxRef.current = 0
      offsetRef.current = 0
      return
    }

    const updatePad = () => {
      const vpH = viewport.clientHeight
      const itemsH = measure.offsetHeight
      if (vpH <= 0 || itemsH <= 0) return
      const pad = Math.max(MIN_GAP_PX, vpH - itemsH)
      setPadPx((prev) => (prev === pad ? prev : pad))
    }

    updatePad()
    const ro = new ResizeObserver(updatePad)
    ro.observe(viewport)
    ro.observe(measure)
    return () => ro.disconnect()
  }, [titles.length, titlesKey])

  // Measure real group height and scroll with rAF (modulo — no keyframe seam).
  useLayoutEffect(() => {
    const track = trackRef.current
    const group = groupRef.current
    if (!track || !group || titles.length === 0) {
      setReady(false)
      return
    }

    const syncCycle = () => {
      // offsetHeight is an integer CSS pixel — avoids fractional getBoundingClientRect
      // mismatches that leave a hairline gap at the loop point on 125%/150% Windows DPI.
      const cycle = group.offsetHeight
      if (cycle <= 0) {
        cyclePxRef.current = 0
        setReady(false)
        return
      }
      if (cyclePxRef.current !== cycle) {
        cyclePxRef.current = cycle
        offsetRef.current = offsetRef.current % cycle
      }
      setReady(true)
    }

    syncCycle()
    void document.fonts?.ready?.then(() => {
      syncCycle()
    })
    const ro = new ResizeObserver(syncCycle)
    ro.observe(group)

    const tick = (ts: number) => {
      const cycle = cyclePxRef.current
      if (cycle > 0 && !document.hidden) {
        if (lastTsRef.current === 0) lastTsRef.current = ts
        // Clamp so a background-tab resume does not jump a full screen.
        const dt = Math.min(0.05, (ts - lastTsRef.current) / 1000)
        lastTsRef.current = ts
        let next = offsetRef.current + SCROLL_PX_PER_SEC * dt
        next %= cycle
        if (next < 0) next += cycle
        offsetRef.current = next
        track.style.transform = `translate3d(0, ${-next}px, 0)`
      } else {
        lastTsRef.current = 0
      }
      rafRef.current = requestAnimationFrame(tick)
    }

    lastTsRef.current = 0
    rafRef.current = requestAnimationFrame(tick)

    const onVis = () => {
      if (document.hidden) lastTsRef.current = 0
    }
    document.addEventListener('visibilitychange', onVis)

    return () => {
      ro.disconnect()
      cancelAnimationFrame(rafRef.current)
      document.removeEventListener('visibilitychange', onVis)
      lastTsRef.current = 0
    }
  }, [padPx, titles.length, titlesKey])

  // Titles changed — start from the top of the loop.
  useLayoutEffect(() => {
    offsetRef.current = 0
    const track = trackRef.current
    if (track) track.style.transform = 'translate3d(0, 0, 0)'
  }, [titlesKey])

  if (titles.length === 0) return null

  return (
    <div className="todos-ticker" ref={viewportRef} aria-hidden>
      <div className="todos-ticker-measure" ref={measureRef}>
        {titles.map((title, i) => (
          <span key={`m-${i}`} className="todos-ticker-item">
            {title}
          </span>
        ))}
      </div>

      <div
        ref={trackRef}
        className={`todos-ticker-track${ready ? ' is-ready' : ''}`}
      >
        {Array.from({ length: COPIES }, (_, copy) => (
          <div
            key={copy}
            ref={copy === 0 ? groupRef : undefined}
            className="todos-ticker-group"
            style={{ paddingBottom: padPx }}
          >
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
