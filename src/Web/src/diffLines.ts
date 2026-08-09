/** Minimal line diff for Markdown history compare (no deps). */

export type DiffLine = { type: 'same' | 'add' | 'del'; text: string }

export function diffLines(a: string, b: string): DiffLine[] {
  const left = a.replace(/\r\n/g, '\n').split('\n')
  const right = b.replace(/\r\n/g, '\n').split('\n')
  // LCS DP for moderate note sizes
  const n = left.length
  const m = right.length
  if (n * m > 400_000) {
    // fallback for huge notes
    return [
      ...left.map((text) => ({ type: 'del' as const, text })),
      ...right.map((text) => ({ type: 'add' as const, text })),
    ]
  }
  const dp: number[][] = Array.from({ length: n + 1 }, () => Array(m + 1).fill(0))
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i]![j] = left[i] === right[j] ? (dp[i + 1]![j + 1]! + 1) : Math.max(dp[i + 1]![j]!, dp[i]![j + 1]!)
    }
  }
  const out: DiffLine[] = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (left[i] === right[j]) {
      out.push({ type: 'same', text: left[i]! })
      i++
      j++
    } else if (dp[i + 1]![j]! >= dp[i]![j + 1]!) {
      out.push({ type: 'del', text: left[i]! })
      i++
    } else {
      out.push({ type: 'add', text: right[j]! })
      j++
    }
  }
  while (i < n) out.push({ type: 'del', text: left[i++]! })
  while (j < m) out.push({ type: 'add', text: right[j++]! })
  return out
}
