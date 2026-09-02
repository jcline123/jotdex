export type OverlayKind =
  | 'none'
  | 'slash'
  | 'plus'
  | 'bubble'
  | 'drag'
  | 'link'
  | 'image'
  | 'table'
  | 'emoji'
  | 'more'

const PRIMARY = new Set<OverlayKind>(['slash', 'plus', 'drag', 'link', 'image', 'table', 'emoji', 'more'])

export class OverlayCoordinator {
  primary: OverlayKind = 'none'
  private listeners = new Set<() => void>()

  subscribe(fn: () => void): () => void {
    this.listeners.add(fn)
    return () => this.listeners.delete(fn)
  }

  open(kind: OverlayKind): void {
    this.primary = kind
    this.emit()
  }

  close(kind?: OverlayKind): void {
    if (!kind || this.primary === kind) this.primary = 'none'
    this.emit()
  }

  closeAll(): void {
    this.primary = 'none'
    this.emit()
  }

  /** True when a command palette / dialog should hide the bubble menu. */
  get blocksBubble(): boolean {
    return PRIMARY.has(this.primary)
  }

  handleEscape(): boolean {
    if (this.primary === 'none') return false
    this.closeAll()
    return true
  }

  private emit(): void {
    for (const fn of this.listeners) fn()
  }
}
