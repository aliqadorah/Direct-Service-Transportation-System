// Direct Service Transportation — small browser helpers for the Blazor components (focus management, shortcuts, device preferences).
// Loaded as an ES module through IJSRuntime ("./js/ui.js"); no inline scripts (CSP).

const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]):not([type="hidden"]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';
const traps = new Map();

/** The user's last activity (keys, pointer, typing, scrolling): record locks are kept only while the user works (BR-039, 90 s). */
let lastActivity = Date.now();
for (const ev of ["keydown", "pointerdown", "input", "wheel"]) document.addEventListener(ev, () => { lastActivity = Date.now(); }, { capture: true, passive: true });
export function idleSeconds() { return Math.floor((Date.now() - lastActivity) / 1000); }

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

/**
 * The key of a shortcut by its physical position (DT-233): "KeyS" is "s" whatever the keyboard language (with the Arabic layout e.key is
 * "س"), "Slash" is "/". Other keys fall back to e.key.
 */
export function shortcutKey(e) {
  if (/^Key[A-Z]$/.test(e.code || "")) return e.code.slice(3).toLowerCase();
  if (e.code === "Slash") return "/";
  return (e.key || "").toLowerCase();
}

/** Ctrl (⌘ on a Mac) without Alt: AltGr is reported as Ctrl+Alt on Windows and types characters, never a shortcut. */
const withCtrl = (e) => (e.ctrlKey || e.metaKey) && !e.altKey;

/**
 * Global shortcuts, one handler for every screen: Ctrl+K / ⌘K opens the command bar (FR-187); "/" focuses it when no field has focus;
 * Ctrl+P prints through the screen's Print button; Ctrl+S saves through the button that shows "Ctrl S".
 */
let shortcutRef = null;
export function onShortcut(e) {
  if (!shortcutRef) return;
  const k = shortcutKey(e);
  const typing = /^(input|textarea|select)$/i.test(e.target.tagName) || e.target.isContentEditable;
  if (withCtrl(e) && k === "k") { e.preventDefault(); shortcutRef.invokeMethodAsync("OpenCommandBar"); }
  else if (k === "/" && !typing && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); shortcutRef.invokeMethodAsync("OpenCommandBar"); }
  else if (withCtrl(e) && k === "p" && document.querySelector('button[data-shortcut="Ctrl P"]')) {
    // Print preview: Ctrl+P prints the logged copy through the screen's Print button, never the unlogged preview (FR-058).
    e.preventDefault();
    const b = document.querySelector('button[data-shortcut="Ctrl P"]:not([disabled])');
    if (b) b.click();
  }
  else if (withCtrl(e) && k === "s") {
    // Ctrl+S never opens the browser's "Save page" dialog in the application (DT-233). It presses the save button of the open dialog (the
    // top one), else the screen's (shown with its shortcut on the button); never the page behind an open dialog.
    e.preventDefault();
    const scope = () => {
      const dialogs = document.querySelectorAll('[aria-modal="true"]');
      return dialogs.length ? dialogs[dialogs.length - 1] : document;
    };
    if (!scope().querySelector('button[data-shortcut="Ctrl S"]')) return;
    // Commit the field being typed in first (fields bind on change, which Enter or leaving the field would also fire), then press the
    // button as it is after that change (re-read: the change may re-render it or enable it).
    const a = document.activeElement;
    if (a && a !== document.body && /^(input|textarea|select)$/i.test(a.tagName)) a.dispatchEvent(new Event("change", { bubbles: true }));
    const b = scope().querySelector('button[data-shortcut="Ctrl S"]:not([disabled])');
    if (b) b.click();
  }
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

/**
 * Collapsed side menu (DT-236): a section's flyout (fixed, so the scrolling menu never clips it) is placed beside the section that is
 * pointed at or focused, kept inside the window. Listeners are added once per menu.
 */
export function navFlyouts(nav) {
  if (!nav || nav.dataset.fly) return;
  nav.dataset.fly = "1";
  const place = (e) => {
    const sec = e.target && e.target.closest ? e.target.closest(".sec") : null;
    const fly = sec && nav.contains(sec) ? sec.querySelector(":scope > .fly") : null;
    if (fly && !fly.contains(e.target)) placeFly(fly);
  };
  nav.addEventListener("pointerover", place);
  nav.addEventListener("focusin", place);
  // An opened flyout stays inside a window that was made smaller.
  window.addEventListener("resize", () => { for (const f of nav.querySelectorAll(".sec.flown > .fly")) placeFly(f, false); });
}

/**
 * Places a flyout beside its section, inside the window. With focus: its entries were just opened with Enter or a click (the flyout grew) —
 * placed again and the first entry focused, once more on the next frame if the focus did not take while the flyout was being shown.
 */
export function placeFly(fly, focus) {
  if (typeof fly === "string") fly = document.getElementById(fly);
  if (!fly || !fly.closest(".side.mini")) return;
  const top = fly.parentElement.getBoundingClientRect().top;
  fly.style.top = Math.max(8, Math.min(top, window.innerHeight - fly.offsetHeight - 8)) + "px";
  if (!focus) return;
  const first = fly.querySelector("a[href]");
  if (!first) return;
  first.focus();
  if (document.activeElement !== first) requestAnimationFrame(() => { placeFly(fly, false); first.focus(); });
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

/** Print preview (SCR-09): loads the document's HTML into the sandboxed frame (no scripts) and opens the browser's print dialog for it. */
export function printHtml(el, html) {
  return new Promise((resolve) => {
    if (!el) { resolve(); return; }
    const done = () => {
      el.removeEventListener("load", done);
      try { el.contentWindow.focus(); el.contentWindow.print(); } catch { /* print unavailable */ }
      resolve();
    };
    el.addEventListener("load", done);
    el.srcdoc = html;
  });
}

/**
 * Places an open combo list as an overlay fixed to the window (owner feedback 13 Oct 2026, SCR-32): a list inside a scrolling table or
 * panel is no longer clipped by it nor covers the next rows inside it. Below the field when there is room, else above; it follows the
 * field while the page or a panel scrolls and on resize, until unfloatList.
 */
const floating = new Map();
export function floatList(wrap, id) {
  const place = () => {
    const list = wrap && wrap.querySelector(".ddlist");
    const input = wrap && wrap.querySelector("input");
    if (!list || !input) return;
    const r = input.getBoundingClientRect();
    const vw = document.documentElement.clientWidth, vh = window.innerHeight;
    const width = Math.min(Math.max(r.width, 300), vw - 16);
    list.style.position = "fixed";
    list.style.minWidth = width + "px";
    list.style.maxWidth = (vw - 16) + "px";
    if (getComputedStyle(wrap).direction === "rtl") {
      list.style.left = "auto";
      list.style.right = Math.max(8, vw - r.right) + "px";
    } else {
      list.style.right = "auto";
      list.style.left = Math.max(8, Math.min(r.left, vw - width - 8)) + "px";
    }
    const below = vh - r.bottom - 8, above = r.top - 8;
    const wantHeight = Math.min(list.scrollHeight, 280);
    if (below < wantHeight && above > below) {
      list.style.top = "auto";
      list.style.bottom = (vh - r.top + 2) + "px";
      list.style.maxHeight = Math.min(280, above) + "px";
    } else {
      list.style.bottom = "auto";
      list.style.top = (r.bottom + 2) + "px";
      list.style.maxHeight = Math.min(280, Math.max(below, 120)) + "px";
    }
  };
  place();
  if (floating.has(id)) return;
  const handler = () => place();
  window.addEventListener("scroll", handler, true);
  window.addEventListener("resize", handler);
  floating.set(id, handler);
}
export function unfloatList(id) {
  const h = floating.get(id);
  if (!h) return;
  window.removeEventListener("scroll", h, true);
  window.removeEventListener("resize", h);
  floating.delete(id);
}
