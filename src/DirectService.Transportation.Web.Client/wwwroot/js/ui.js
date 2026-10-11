// Direct Service Transportation — small browser helpers for the Blazor components (focus management, shortcuts, device preferences).
// Loaded as an ES module through IJSRuntime ("./js/ui.js"); no inline scripts (CSP).

const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]):not([type="hidden"]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';
const traps = new Map();

/** Keeps Tab / Shift+Tab inside a dialog, focuses its first field, and restores focus to the opener when released (WCAG 2.4.3). */
export function trapFocus(el, id) {
  if (!el) return;
  const opener = document.activeElement;
  const onKey = (e) => {
    if (e.key !== "Tab") return;
    const items = [...el.querySelectorAll(FOCUSABLE)].filter((x) => x.offsetParent !== null);
    if (items.length === 0) { e.preventDefault(); return; }
    const first = items[0], last = items[items.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  el.addEventListener("keydown", onKey);
  traps.set(id, { el, onKey, opener });
  const auto = el.querySelector("[autofocus]") || el.querySelector(".db " + FOCUSABLE) || el.querySelector(FOCUSABLE);
  if (auto) setTimeout(() => auto.focus(), 0);
}

export function releaseFocus(id) {
  const t = traps.get(id);
  if (!t) return;
  t.el.removeEventListener("keydown", t.onKey);
  traps.delete(id);
  if (t.opener && document.contains(t.opener)) t.opener.focus();
}

/** Global shortcuts: Ctrl+K / ⌘K opens the command bar (FR-187); "/" focuses it when no field has focus. */
let shortcutRef = null;
function onShortcut(e) {
  if (!shortcutRef) return;
  const k = e.key.toLowerCase();
  const typing = /^(input|textarea|select)$/i.test(e.target.tagName) || e.target.isContentEditable;
  if ((e.ctrlKey || e.metaKey) && k === "k") { e.preventDefault(); shortcutRef.invokeMethodAsync("OpenCommandBar"); }
  else if (k === "/" && !typing) { e.preventDefault(); shortcutRef.invokeMethodAsync("OpenCommandBar"); }
}
export function registerShortcuts(ref) {
  shortcutRef = ref;
  document.removeEventListener("keydown", onShortcut);
  document.addEventListener("keydown", onShortcut);
}
export function unregisterShortcuts() { shortcutRef = null; document.removeEventListener("keydown", onShortcut); }

/** Closes a popup (menu, combo list) when the user clicks or focuses outside it. */
const outside = new Map();
export function onOutside(el, ref, id) {
  const h = (e) => { if (el && !el.contains(e.target)) ref.invokeMethodAsync("Outside"); };
  document.addEventListener("pointerdown", h, true);
  document.addEventListener("focusin", h, true);
  outside.set(id, h);
}
export function offOutside(id) {
  const h = outside.get(id);
  if (!h) return;
  document.removeEventListener("pointerdown", h, true);
  document.removeEventListener("focusin", h, true);
  outside.delete(id);
}

/** Per-device preferences (DT-69: density is stored in the browser, not in core.Users). Storage may be unavailable. */
export function getPref(key) { try { return localStorage.getItem("dst." + key); } catch { return null; } }
export function setPref(key, value) { try { localStorage.setItem("dst." + key, value); } catch { /* storage unavailable */ } }

/** Applies density and language direction to the document root. */
export function applyRoot(density, lang) {
  const r = document.documentElement;
  r.dataset.density = density === "comfortable" ? "comfortable" : "compact";
  if (lang) { r.lang = lang; r.dir = lang === "en" ? "ltr" : "rtl"; }
}

/** The language the server rendered into <html lang> (profile language, or the browser's for anonymous pages). */
export function rootLang() { return document.documentElement.lang || "ar"; }

export function focusById(id) { const el = document.getElementById(id); if (el) { el.focus(); el.scrollIntoView({ block: "nearest" }); } }

export function scrollIntoView(el) { if (el) el.scrollIntoView({ block: "nearest" }); }

/** Combo box: arrow keys move in the list instead of the caret, Enter picks instead of submitting the form while the list is open. */
export function guardComboKeys(el) {
  if (!el || el.dataset.guarded) return;
  el.dataset.guarded = "1";
  el.addEventListener("keydown", (e) => {
    const open = el.getAttribute("aria-expanded") === "true";
    if (e.key === "ArrowDown" || e.key === "ArrowUp" || (open && (e.key === "Enter" || e.key === "Escape"))) e.preventDefault();
  });
}

/** Menu keyboard focus: delta 0 = first item, 9999 = last, ±1 = next/previous (wrapping). */
export function focusMenu(id, delta) {
  setTimeout(() => {
    const menu = document.getElementById(id);
    if (!menu) return;
    const items = [...menu.querySelectorAll('[role^="menuitem"]:not([disabled])')];
    if (items.length === 0) return;
    const i = items.indexOf(document.activeElement);
    let n = delta === 0 ? 0 : delta >= 9999 ? items.length - 1 : i < 0 ? 0 : (i + delta + items.length) % items.length;
    items[n].focus();
  }, 0);
}

export function focusFirst(el) { const b = el && el.querySelector(FOCUSABLE); if (b) b.focus(); }

/** Opens the browser's date picker of a (visually hidden) date input. */
export function showPicker(el) { try { el.showPicker(); } catch { el.focus(); } }

/** Saves a file received from the API (exports, PDFs) through a temporary object URL. */
export function saveFile(name, contentType, bytes) {
  const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = name || "download"; a.rel = "noopener";
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
