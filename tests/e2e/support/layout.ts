import type { Page } from "@playwright/test";

/**
 * Layout problems a user sees on screen, found from the rendered boxes (no image comparison):
 * - overlap: two pieces of visible text painted over each other (different text nodes whose line boxes intersect by more than a sliver),
 *   unless something opaque — a dialog, a dropdown, a sticky header — covers one of them;
 * - cut-off: an element with its own text whose text is wider or taller than a box that clips it (overflow hidden/clip) without an ellipsis;
 * - page-overflow: the page itself scrolls sideways.
 * Only what is inside the viewport and inside every clipping ancestor counts; screen readers' visually hidden text is ignored.
 */
export type LayoutProblem = { kind: "overlap" | "cut-off" | "page-overflow"; text: string; other?: string; where: string };

export async function layoutProblems(page: Page): Promise<LayoutProblem[]> {
  return page.evaluate(() => {
    type Box = { l: number; t: number; r: number; b: number };
    const problems: { kind: "overlap" | "cut-off" | "page-overflow"; text: string; other?: string; where: string }[] = [];
    const vw = document.documentElement.clientWidth, vh = document.documentElement.clientHeight;
    const skipTags = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEMPLATE", "SELECT", "OPTION", "TEXTAREA", "TITLE", "IFRAME"]);

    const path = (el: Element): string => {
      const parts: string[] = [];
      for (let e: Element | null = el; e && e !== document.body && parts.length < 4; e = e.parentElement) {
        const cls = typeof e.className === "string" && e.className.trim() ? "." + e.className.trim().split(/\s+/).slice(0, 2).join(".") : "";
        parts.unshift(e.tagName.toLowerCase() + cls);
      }
      return parts.join(" > ");
    };
    const shown = (el: Element): boolean => {
      for (let e: Element | null = el; e; e = e.parentElement) {
        const s = getComputedStyle(e);
        if (s.display === "none" || s.visibility === "hidden" || s.visibility === "collapse" || Number(s.opacity) < 0.05) return false;
      }
      return true;
    };
    // The part of a box that is actually painted: inside the viewport and every ancestor that clips its overflow.
    const clipCache = new Map<Element, Box>();
    const clipOf = (el: Element): Box => {
      const hit = clipCache.get(el);
      if (hit) return hit;
      let box: Box = { l: 0, t: 0, r: vw, b: vh };
      const parent = el.parentElement;
      if (parent) box = clipOf(parent);
      const s = getComputedStyle(el);
      if (s.position === "fixed") box = { l: 0, t: 0, r: vw, b: vh };
      if (el !== document.documentElement && el !== document.body && (s.overflowX !== "visible" || s.overflowY !== "visible")) {
        const r = el.getBoundingClientRect();
        box = { l: Math.max(box.l, r.left), t: Math.max(box.t, r.top), r: Math.min(box.r, r.right), b: Math.min(box.b, r.bottom) };
      }
      clipCache.set(el, box);
      return box;
    };
    const opaque = (el: Element): boolean => {
      // A cell also paints its row's and row group's background (rows and groups are not returned by elementsFromPoint).
      if ((el.tagName === "TD" || el.tagName === "TH") && el.parentElement) {
        const row = el.parentElement, group = row.parentElement;
        if (solid(el) || solid(row) || (group !== null && group.tagName !== "TABLE" && solid(group))) return true;
        return false;
      }
      return solid(el);
    };
    const solid = (el: Element): boolean => {
      const s = getComputedStyle(el);
      if (s.backgroundImage !== "none") return true;
      const m = /rgba?\(([^)]+)\)/.exec(s.backgroundColor);
      if (!m) return false;
      const parts = m[1].split(",").map((x) => Number(x.trim()));
      return parts.length < 4 || parts[3] > 0.5;
    };
    // Something opaque painted above `el` at this point (a dialog over the page, a dropdown, a sticky header)?
    const covered = (el: Element, x: number, y: number): boolean => {
      for (const top of document.elementsFromPoint(x, y)) {
        if (top === el || top.contains(el)) return false;   // reached the text's own layer
        if (el.contains(top)) continue;
        if (opaque(top)) return true;
      }
      return false;
    };

    // ---- visible text line boxes
    type Piece = { node: Text; el: Element; box: Box; text: string };
    const pieces: Piece[] = [];
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const range = document.createRange();
    const shownCache = new Map<Element, boolean>();
    for (let n = walker.nextNode() as Text | null; n; n = walker.nextNode() as Text | null) {
      const text = (n.textContent ?? "").replace(/\s+/g, " ").trim();
      const el = n.parentElement;
      if (!text || !el || skipTags.has(el.tagName) || el.closest("svg, select, textarea")) continue;
      let vis = shownCache.get(el);
      if (vis === undefined) { vis = shown(el); shownCache.set(el, vis); }
      if (!vis) continue;
      const clip = clipOf(el);
      range.selectNodeContents(n);
      for (const r of Array.from(range.getClientRects())) {
        const box = { l: Math.max(r.left, clip.l), t: Math.max(r.top, clip.t), r: Math.min(r.right, clip.r), b: Math.min(r.bottom, clip.b) };
        if (box.r - box.l < 2 || box.b - box.t < 4) continue;   // clipped away, or visually hidden (1 px)
        pieces.push({ node: n, el, box, text });
      }
    }

    // ---- overlapping text
    pieces.sort((a, b) => a.box.t - b.box.t);
    const seen = new Set<string>();
    for (let i = 0; i < pieces.length; i++) {
      const a = pieces[i];
      for (let j = i + 1; j < pieces.length && pieces[j].box.t < a.box.b; j++) {
        const b = pieces[j];
        if (a.node === b.node) continue;
        const w = Math.min(a.box.r, b.box.r) - Math.max(a.box.l, b.box.l);
        const h = Math.min(a.box.b, b.box.b) - Math.max(a.box.t, b.box.t);
        if (w < 3 || h <= 0) continue;
        // Line boxes of neighbouring lines touch or overlap slightly (tall Arabic glyph boxes): require a real share of the shorter line.
        const minH = Math.min(a.box.b - a.box.t, b.box.b - b.box.t);
        if (h < Math.max(4, minH * 0.4)) continue;
        const x = Math.max(a.box.l, b.box.l) + w / 2, y = Math.max(a.box.t, b.box.t) + h / 2;
        if (covered(a.el, x, y) || covered(b.el, x, y)) continue;
        const key = `${a.text}|${b.text}`;
        if (seen.has(key)) continue;
        seen.add(key);
        problems.push({ kind: "overlap", text: a.text.slice(0, 60), other: b.text.slice(0, 60), where: path(a.el) });
      }
    }

    // ---- a picture or a form control over text (a QR code over the instructions, a field over its label)
    const graphics: { el: Element; box: Box }[] = [];
    for (const el of Array.from(document.querySelectorAll("img, canvas, svg, input:not([type=hidden]), select, textarea"))) {
      if (el.tagName.toLowerCase() === "svg" && el.parentElement?.closest("svg")) continue;
      if (!shown(el)) continue;
      const r = el.getBoundingClientRect(), clip = clipOf(el);
      // A form control's text area is its content box: units and icons drawn in its padding (a "t" or "%" adornment) are by design.
      const s = getComputedStyle(el), control = /^(INPUT|SELECT|TEXTAREA)$/.test(el.tagName);
      const inset = (side: string) => (control ? parseFloat(s.getPropertyValue(`padding-${side}`)) + parseFloat(s.getPropertyValue(`border-${side}-width`)) : 0);
      const box = { l: Math.max(r.left + inset("left"), clip.l), t: Math.max(r.top + inset("top"), clip.t),
        r: Math.min(r.right - inset("right"), clip.r), b: Math.min(r.bottom - inset("bottom"), clip.b) };
      if (box.r - box.l >= 8 && box.b - box.t >= 8) graphics.push({ el, box });
    }
    // Opaque layers above `el` other than the graphic itself (a dialog or panel holding the graphic hides the page text beneath it).
    const coveredExcept = (el: Element, g: Element, x: number, y: number): boolean => {
      for (const top of document.elementsFromPoint(x, y)) {
        if (top === el || top.contains(el)) return false;
        if (el.contains(top) || top === g || g.contains(top)) continue;
        if (opaque(top)) return true;
      }
      return false;
    };
    for (const g of graphics) {
      for (const a of pieces) {
        if (g.el.contains(a.el) || a.el.contains(g.el)) continue;
        const w = Math.min(a.box.r, g.box.r) - Math.max(a.box.l, g.box.l);
        const h = Math.min(a.box.b, g.box.b) - Math.max(a.box.t, g.box.t);
        if (w < 3 || h < 4 || w * h < 0.25 * (a.box.r - a.box.l) * (a.box.b - a.box.t)) continue;
        const x = Math.max(a.box.l, g.box.l) + w / 2, y = Math.max(a.box.t, g.box.t) + h / 2;
        if (coveredExcept(a.el, g.el, x, y) || covered(g.el, x, y)) continue;   // either one hidden (e.g. rows under a sticky total row)
        const key = `g|${a.text}|${path(g.el)}`;
        if (seen.has(key)) continue;
        seen.add(key);
        problems.push({ kind: "overlap", text: a.text.slice(0, 60), other: `<${g.el.tagName.toLowerCase()}>`, where: path(a.el) });
      }
    }

    // ---- text cut off by its own clipping box
    const owners = new Set(pieces.map((p) => p.el));
    for (const el of owners) {
      for (let e: Element | null = el; e && e !== document.body; e = e.parentElement) {
        const s = getComputedStyle(e);
        const clipsX = s.overflowX === "hidden" || s.overflowX === "clip", clipsY = s.overflowY === "hidden" || s.overflowY === "clip";
        if (!clipsX && !clipsY) {
          if (/(auto|scroll)/.test(s.overflowX + s.overflowY)) break;   // reachable by scrolling
          continue;
        }
        if (s.textOverflow === "ellipsis" || e.matches(".sr-only, .visually-hidden") || e.getBoundingClientRect().width <= 2) break;
        const he = e as HTMLElement;
        const over = (clipsX && he.scrollWidth > he.clientWidth + 1) || (clipsY && he.scrollHeight > he.clientHeight + 2);
        if (over && e.contains(el) && (clipsX ? he.clientWidth > 0 : he.clientHeight > 0)) {
          const key = `cut|${path(e)}`;
          if (!seen.has(key)) {
            seen.add(key);
            problems.push({ kind: "cut-off", text: (he.innerText || el.textContent || "").replace(/\s+/g, " ").trim().slice(0, 60), where: path(e) });
          }
        }
        break;   // only the nearest clipping box
      }
    }

    // ---- text spilling out of a control of fixed size (a button label wrapping onto a second line below a 32 px button)
    // Measured from the text's line boxes (buttons do not report scrollHeight reliably).
    for (const a of pieces) {
      const c = a.el.closest("button, a.btn, .btn, .badge, [role=tab], [role=menuitem], [role=option]");
      if (!c) continue;
      const he = c as HTMLElement, s = getComputedStyle(he);
      if (s.overflowX !== "visible" || s.overflowY !== "visible") continue;   // clipping boxes are checked above
      const r = c.getBoundingClientRect();
      if (a.box.t < r.top - 2 || a.box.b > r.bottom + 2 || a.box.l < r.left - 1 || a.box.r > r.right + 1) {
        const key = `spill|${path(c)}|${he.innerText}`;
        if (!seen.has(key)) {
          seen.add(key);
          problems.push({ kind: "cut-off", text: `spills out: ${(he.innerText || "").replace(/\s+/g, " ").trim().slice(0, 60)}`, where: path(c) });
        }
      }
    }

    // ---- a one-line control (button, tab, badge) whose label wraps onto a second line: misaligned toolbars and bars
    const lines = new Map<Element, number[]>();
    for (const a of pieces) {
      const c = a.el.closest("button, a.btn, .btn, .badge, [role=tab]");
      if (!c || c.closest("[role=listbox], [role=menu], .steps")) continue;
      const tops = lines.get(c) ?? [];
      tops.push((a.box.t + a.box.b) / 2);
      lines.set(c, tops);
    }
    for (const [c, mids] of lines) {
      const sorted = [...mids].sort((x, y) => x - y);
      const fs = parseFloat(getComputedStyle(c).fontSize) || 14;
      if (sorted[sorted.length - 1] - sorted[0] > fs * 0.9) {
        const key = `wrap|${path(c)}`;
        if (!seen.has(key)) {
          seen.add(key);
          problems.push({ kind: "cut-off", text: `wraps: ${((c as HTMLElement).innerText || "").replace(/\s+/g, " ").trim().slice(0, 60)}`, where: path(c) });
        }
      }
    }

    // ---- sideways page scroll
    if (document.documentElement.scrollWidth > vw + 1) {
      problems.push({ kind: "page-overflow", text: `page is ${document.documentElement.scrollWidth}px wide in a ${vw}px window`, where: "html" });
    }
    return problems;
  });
}
