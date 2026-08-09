/** Shared clip / bookmarklet helpers — keep bookmarklet logic in one place. */

export type ClipPayload = {
  title?: string
  url?: string
  text?: string
  html?: string
}

export const CLIP_DEFAULT_FOLDER_KEY = 'jotdex.clipDefaultFolder'
export const CLIP_HASH_PREFIX = '#clip='

export function loadClipDefaultFolder(): string {
  try {
    const v = localStorage.getItem(CLIP_DEFAULT_FOLDER_KEY)
    if (v != null && v.trim() !== '') return v.trim().replace(/\\/g, '/')
  } catch {
    /* ignore */
  }
  return 'Inbox'
}

export function saveClipDefaultFolder(folder: string) {
  try {
    localStorage.setItem(CLIP_DEFAULT_FOLDER_KEY, folder.replace(/\\/g, '/'))
  } catch {
    /* ignore */
  }
}

/** Parse `#clip=...` JSON from a capture URL (hash only — never sent to the server as a query). */
export function parseClipHash(hash: string): ClipPayload | null {
  if (!hash || !hash.startsWith(CLIP_HASH_PREFIX)) return null
  try {
    const raw = decodeURIComponent(hash.slice(CLIP_HASH_PREFIX.length))
    const data = JSON.parse(raw) as ClipPayload
    if (!data || typeof data !== 'object') return null
    return {
      title: typeof data.title === 'string' ? data.title : undefined,
      url: typeof data.url === 'string' ? data.url : undefined,
      text: typeof data.text === 'string' ? data.text : undefined,
      html: typeof data.html === 'string' ? data.html : undefined,
    }
  } catch {
    return null
  }
}

/**
 * Bookmarklet that opens the main Jotdex app with the page title, URL, and selection
 * in the hash. Same-origin window = session cookie + lock screen still apply.
 */
export function buildClipBookmarklet(origin: string): string {
  const base = origin.replace(/\/+$/, '')
  return (
    `javascript:(function(){` +
    `var o=${JSON.stringify(base)};` +
    `var t=document.title||'';` +
    `var u=location.href||'';` +
    `var s='';` +
    `try{s=String(window.getSelection&&window.getSelection()||'')}catch(e){}` +
    `if(s.length>80000)s=s.slice(0,80000);` +
    `var p={title:t,url:u,text:s};` +
    `var h=${JSON.stringify(CLIP_HASH_PREFIX)}+encodeURIComponent(JSON.stringify(p));` +
    `var w=window.open(o+'/'+h,'jotdex_clip','popup=yes,width=560,height=720,noopener=no');` +
    `if(!w)location.href=o+'/'+h;` +
    `})();`
  )
}
