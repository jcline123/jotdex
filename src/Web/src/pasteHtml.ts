/** Clean HTML pasted from browsers so TipTap keeps structure without scripts/cruft. */

const ALLOWED_TAGS = new Set([
  'A',
  'B',
  'BLOCKQUOTE',
  'BR',
  'CODE',
  'DIV',
  'EM',
  'FIGCAPTION',
  'FIGURE',
  'H1',
  'H2',
  'H3',
  'H4',
  'H5',
  'H6',
  'HR',
  'I',
  'IMG',
  'LI',
  'OL',
  'P',
  'PRE',
  'S',
  'SPAN',
  'STRONG',
  'SUB',
  'SUP',
  'TABLE',
  'TBODY',
  'TD',
  'TH',
  'THEAD',
  'TR',
  'U',
  'UL',
])

const STYLE_TAGS = new Set(['SPAN', 'P', 'DIV', 'TD', 'TH', 'LI', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6'])

export type CleanPasteOptions = {
  /** Keep more inline styles (color, size, weight, align). Default false = smart. */
  keepMore?: boolean
}

export function cleanPasteHtml(raw: string, options: CleanPasteOptions = {}): string {
  const unescaped = maybeUnescapeHtml(raw)
  const doc = new DOMParser().parseFromString(wrapFragment(unescaped), 'text/html')
  const body = doc.body
  stripDangerous(body, options.keepMore === true)
  normalizeDivs(body)
  if (options.keepMore !== true) splitBrIntoParagraphs(body)
  absolutizeProtocolRelativeImages(body)
  return body.innerHTML.trim()
}

export function extractHttpImageUrls(html: string): string[] {
  const doc = new DOMParser().parseFromString(wrapFragment(html), 'text/html')
  const urls: string[] = []
  for (const img of Array.from(doc.querySelectorAll('img[src]'))) {
    const src = img.getAttribute('src')?.trim() ?? ''
    if (/^https?:\/\//i.test(src)) urls.push(src)
  }
  return [...new Set(urls)]
}

/** Replace data: images with temporary https markers TipTap can parse. */
export function rewriteDataImages(html: string): {
  html: string
  items: { marker: string; mime: string; bytesBase64: string; fileName: string }[]
} {
  const doc = new DOMParser().parseFromString(wrapFragment(html), 'text/html')
  const items: { marker: string; mime: string; bytesBase64: string; fileName: string }[] = []
  for (const img of Array.from(doc.querySelectorAll('img[src^="data:image"]'))) {
    const src = img.getAttribute('src') ?? ''
    const m = /^data:(image\/[a-z0-9.+-]+);base64,([a-z0-9+/=\s]+)$/i.exec(src)
    if (!m) continue
    const marker = `https://paste.invalid/local-${items.length}-${Math.random().toString(36).slice(2, 8)}`
    const ext = m[1].split('/')[1]?.replace('jpeg', 'jpg') ?? 'png'
    items.push({
      marker,
      mime: m[1],
      bytesBase64: m[2].replace(/\s+/g, ''),
      fileName: `pasted-${items.length + 1}.${ext}`,
    })
    img.setAttribute('src', marker)
  }
  return { html: doc.body.innerHTML, items }
}

export function dataUrlToFile(mime: string, base64: string, fileName: string): File {
  const bin = atob(base64)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return new File([bytes], fileName, { type: mime })
}

function maybeUnescapeHtml(html: string): string {
  const trimmed = html.trim()
  // Clipboard sometimes contains HTML that was entity-escaped into text.
  const looksEscaped =
    /&lt;(p|div|span|h[1-6]|ul|ol|li|table|img|a|br)\b/i.test(trimmed) &&
    !/<(p|div|span|h[1-6]|ul|ol|li|table|img|a|br)\b/i.test(trimmed.slice(0, 500))
  if (!looksEscaped) return html
  const ta = document.createElement('textarea')
  ta.innerHTML = trimmed
  return ta.value
}

function wrapFragment(raw: string): string {
  if (/<html[\s>]/i.test(raw) || /<body[\s>]/i.test(raw)) return raw
  return `<!DOCTYPE html><html><body>${raw}</body></html>`
}

function absolutizeProtocolRelativeImages(root: Element) {
  for (const img of Array.from(root.querySelectorAll('img[src^="//"]'))) {
    const src = img.getAttribute('src')
    if (src) img.setAttribute('src', `https:${src}`)
  }
}

function stripDangerous(root: Element, keepMore: boolean) {
  for (const el of Array.from(root.querySelectorAll('script,style,iframe,object,embed,link,meta,noscript'))) {
    el.remove()
  }

  const process = (el: HTMLElement) => {
    for (const child of Array.from(el.children)) {
      process(child as HTMLElement)
    }

    if (!ALLOWED_TAGS.has(el.tagName)) {
      const parent = el.parentNode
      if (parent) {
        while (el.firstChild) parent.insertBefore(el.firstChild, el)
        parent.removeChild(el)
      }
      return
    }

    for (const attr of Array.from(el.attributes)) {
      const name = attr.name.toLowerCase()
      if (name.startsWith('on') || name === 'srcset' || name === 'class' || name === 'id') {
        el.removeAttribute(attr.name)
        continue
      }
      if (el.tagName === 'A' && name === 'href') {
        const href = attr.value.trim()
        if (/^\s*javascript:/i.test(href)) el.removeAttribute('href')
        continue
      }
      if (el.tagName === 'IMG' && (name === 'src' || name === 'alt' || name === 'title')) continue
      if (name === 'style' && STYLE_TAGS.has(el.tagName)) {
        const color = el.style.color
        const fontSize = el.style.fontSize
        const fontWeight = el.style.fontWeight
        const textAlign = el.style.textAlign
        const backgroundColor = keepMore ? el.style.backgroundColor : ''
        el.removeAttribute('style')
        if (color) el.style.color = color
        if (fontSize) el.style.fontSize = fontSize
        if (keepMore) {
          if (fontWeight) el.style.fontWeight = fontWeight
          if (textAlign) el.style.textAlign = textAlign
          if (backgroundColor) el.style.backgroundColor = backgroundColor
        }
        continue
      }
      if (name === 'colspan' || name === 'rowspan') continue
      if (el.tagName === 'A' && name === 'target') continue
      el.removeAttribute(attr.name)
    }
  }

  for (const child of Array.from(root.children)) {
    process(child as HTMLElement)
  }
}

/**
 * Smart paste: turn <br>-separated lines inside paragraphs into real paragraphs
 * so pasted content gets the same spacing as typed content (hard breaks store
 * as trailing `\` in Markdown and render tighter than paragraph breaks).
 * Breaks inside list items and table cells stay, since a paragraph there would
 * change the structure.
 */
function splitBrIntoParagraphs(root: Element) {
  const doc = root.ownerDocument!
  for (const p of Array.from(root.querySelectorAll('p'))) {
    if (!p.querySelector('br')) continue
    if (p.closest('li, td, th')) continue

    const groups: Node[][] = [[]]
    for (const child of Array.from(p.childNodes)) {
      if (child.nodeName === 'BR') {
        if (groups[groups.length - 1].length > 0) groups.push([])
      } else {
        groups[groups.length - 1].push(child)
      }
    }

    const frag = doc.createDocumentFragment()
    for (const group of groups) {
      if (group.length === 0) continue
      const next = doc.createElement('p')
      for (const attr of Array.from(p.attributes)) next.setAttribute(attr.name, attr.value)
      for (const node of group) next.appendChild(node)
      if ((next.textContent?.trim().length ?? 0) > 0 || next.querySelector('img')) {
        frag.appendChild(next)
      }
    }
    if (frag.childNodes.length > 0) p.replaceWith(frag)
    else p.remove()
  }
}

function normalizeDivs(root: Element) {
  for (const div of Array.from(root.querySelectorAll('div'))) {
    const onlyInline =
      !div.querySelector('p,div,ul,ol,table,pre,h1,h2,h3,h4,h5,h6,blockquote,figure') &&
      (div.textContent?.trim().length ?? 0) > 0
    if (onlyInline) {
      const p = root.ownerDocument!.createElement('p')
      while (div.firstChild) p.appendChild(div.firstChild)
      div.replaceWith(p)
    }
  }
}
