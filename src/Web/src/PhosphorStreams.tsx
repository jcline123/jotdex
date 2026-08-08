import { useEffect, useRef } from 'react'

const GLYPHS = '01ABCDEF<>[]{}/\\|-=+*#.·'

type Column = {
  x: number
  y: number
  speed: number
  fontSize: number
  chars: string[]
}

/**
 * Soft vertical phosphor code streams (Swordfish-ish) for lock/login only.
 * Canvas 2D, paused when the tab is hidden; low opacity so the form stays readable.
 */
export function PhosphorStreams({ className }: { className?: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    let columns: Column[] = []
    let raf = 0
    let running = true
    let last = performance.now()

    const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)')

    function resize() {
      const parent = canvas!.parentElement
      const w = parent?.clientWidth ?? window.innerWidth
      const h = parent?.clientHeight ?? window.innerHeight
      const dpr = Math.min(window.devicePixelRatio || 1, 2)
      canvas!.width = Math.floor(w * dpr)
      canvas!.height = Math.floor(h * dpr)
      canvas!.style.width = `${w}px`
      canvas!.style.height = `${h}px`
      ctx!.setTransform(dpr, 0, 0, dpr, 0, 0)
      seed(w, h)
    }

    function seed(w: number, h: number) {
      const count = Math.max(18, Math.min(42, Math.floor(w / 36)))
      columns = Array.from({ length: count }, (_, i) => {
        const fontSize = 11 + (i % 4)
        const len = 8 + (i % 10)
        return {
          x: (i + 0.5) * (w / count) + ((i * 17) % 11) - 5,
          y: Math.random() * h,
          speed: 18 + (i % 7) * 6 + Math.random() * 12,
          fontSize,
          chars: Array.from({ length: len }, () => GLYPHS[(i * 3 + len) % GLYPHS.length]!),
        }
      })
    }

    function paint(now: number) {
      if (!running) return
      const dt = Math.min(0.05, (now - last) / 1000)
      last = now
      const w = canvas!.clientWidth
      const h = canvas!.clientHeight

      ctx!.fillStyle = 'rgba(6, 10, 16, 0.18)'
      ctx!.fillRect(0, 0, w, h)

      if (!prefersReduced.matches) {
        ctx!.font = '12px ui-monospace, Cascadia Code, Consolas, monospace'
        ctx!.textAlign = 'center'

        for (const col of columns) {
          col.y += col.speed * dt
          if (col.y - col.chars.length * col.fontSize > h) {
            col.y = -Math.random() * h * 0.3
            col.speed = 18 + Math.random() * 40
            for (let i = 0; i < col.chars.length; i++) {
              if (Math.random() < 0.35) col.chars[i] = GLYPHS[(Math.random() * GLYPHS.length) | 0]!
            }
          }

          ctx!.font = `${col.fontSize}px ui-monospace, Cascadia Code, Consolas, monospace`
          for (let i = 0; i < col.chars.length; i++) {
            const gy = col.y - i * (col.fontSize + 2)
            if (gy < -20 || gy > h + 20) continue
            const head = i === 0
            const alpha = head ? 0.55 : Math.max(0.06, 0.32 - i * 0.028)
            ctx!.fillStyle = head
              ? `rgba(120, 220, 210, ${alpha})`
              : `rgba(56, 160, 150, ${alpha})`
            ctx!.fillText(col.chars[i]!, col.x, gy)
          }

          if (Math.random() < 0.04) {
            const idx = (Math.random() * col.chars.length) | 0
            col.chars[idx] = GLYPHS[(Math.random() * GLYPHS.length) | 0]!
          }
        }
      } else {
        // Static faint grid for reduced-motion users
        ctx!.strokeStyle = 'rgba(56, 160, 150, 0.08)'
        ctx!.lineWidth = 1
        for (let x = 24; x < w; x += 48) {
          ctx!.beginPath()
          ctx!.moveTo(x, 0)
          ctx!.lineTo(x, h)
          ctx!.stroke()
        }
      }

      raf = requestAnimationFrame(paint)
    }

    function onVis() {
      if (document.visibilityState === 'hidden') {
        running = false
        cancelAnimationFrame(raf)
      } else {
        running = true
        last = performance.now()
        raf = requestAnimationFrame(paint)
      }
    }

    resize()
    ctx.fillStyle = '#060a10'
    ctx.fillRect(0, 0, canvas.clientWidth, canvas.clientHeight)
    raf = requestAnimationFrame(paint)
    window.addEventListener('resize', resize)
    document.addEventListener('visibilitychange', onVis)

    return () => {
      running = false
      cancelAnimationFrame(raf)
      window.removeEventListener('resize', resize)
      document.removeEventListener('visibilitychange', onVis)
    }
  }, [])

  return (
    <canvas
      ref={canvasRef}
      className={className ?? 'phosphor-streams'}
      aria-hidden="true"
    />
  )
}
