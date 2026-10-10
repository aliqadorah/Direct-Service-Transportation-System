/* Direct Service — step 6 mockups: shared components, sample data, shell, states and review controls. */
window.DS = (function () {
  "use strict";
  const LOGO = "logo.png";

  // ------------------------------------------------------------------ primitives
  const ic = (n, cls = "") => `<svg class="ic ${cls}" aria-hidden="true"><use href="#i-${n}"/></svg>`;
  const N = (v) => `<span class="num">${v}</span>`;
  /**
   * Isolates every left-to-right run inside Arabic text (codes such as TRP-10481 or INV-02210, dates, amounts, IBANs,
   * "Volvo FH", "Ctrl K") as ONE unit, so the run keeps its internal order in RTL. Runs of Latin tokens joined by spaces,
   * "·" or "/" stay together. Markup inside the string is left untouched.
   */
  const TOKEN = "[A-Za-z0-9](?:[A-Za-z0-9.,:%٪+\\-−–_@']*[A-Za-z0-9%٪])?";
  const RUN = new RegExp(`(${TOKEN}(?:(?:\\s*[·/]\\s*|\\s+)${TOKEN})*)`, "g");
  const T = (s) => String(s).split(/(<[^>]*>|&[a-z#0-9]+;)/i).map((part, i) => (i % 2 ? part : part.replace(RUN, '<span class="num">$1</span>'))).join("");
  const badge = (k, text, icon) => `<span class="badge b-${k}">${icon ? ic(icon) : ""}${T(text)}</span>`;
  const STATUS = {
    unbilled: ["info", "غير مفوترة", "clock"], invoiced: ["ok", "مفوترة", "check"], settled: ["mute", "مسوّاة", "check"], void: ["err", "ملغاة", "x"],
    short: ["warn", "نقص", "scale"], breach: ["err", "مخالفة تعرفة", "alert"], manual: ["warn", "سعر يدوي", "edit"], review: ["warn", "تحتاج مراجعة", "alert"],
    draft: ["mute", "مسودة", "edit"], unpaid: ["info", "معتمدة – غير مدفوعة", "clock"], partial: ["warn", "مدفوعة جزئياً", "clock"], closed: ["ok", "مغلقة", "check"],
    posted: ["ok", "مرحّل", "check"], pending: ["warn", "معلّق", "clock"], reversed: ["mute", "معكوس", "refresh"],
    active: ["ok", "نشط", "check"], archived: ["mute", "مؤرشف", "archive"], open: ["ok", "مفتوحة", "check"], closedP: ["mute", "مغلقة", "lock"],
    queued: ["info", "في الطابور", "clock"], submitted: ["ok", "مقبولة", "check"], rejected: ["err", "مرفوضة", "x"], notreq: ["mute", "غير مطلوبة", ""],
    mapped: ["ok", "مربوط", "check"], missing: ["err", "غير مربوط", "alert"], disabled: ["mute", "معطّل", "lock"], invited: ["info", "مدعو", "mail"],
    trial: ["info", "تجريبي", "clock"], pastdue: ["warn", "متأخر الدفع", "alert"], suspended: ["err", "موقوف", "lock"],
  };
  const st = (k) => { const s = STATUS[k]; return s ? badge(s[0], s[1], s[2]) : k; };
  const btn = (label, o = {}) => `<button class="btn ${o.pri ? "pri" : ""} ${o.ghost ? "ghost" : ""} ${o.danger ? "danger" : ""} ${o.dangerpri ? "dangerpri" : ""} ${o.sm ? "sm" : ""}" ${o.dis ? "disabled" : ""} ${o.aria ? `aria-label="${o.aria}"` : ""}>${o.icon ? ic(o.icon) : ""}${label ? T(label) : ""}${o.kbd ? ` <span class="kbd">${o.kbd}</span>` : ""}</button>`;
  const banner = (k, text, action, icon) => `<div class="banner ${k}">${ic(icon || ({ warn: "alert", err: "alert", info: "alert", ok: "check", lock: "lock" }[k]))}<span>${T(text)}</span>${action ? `<span class="a">${action}</span>` : ""}</div>`;
  const toast = (text, link) => `<div class="toast" role="status">${ic("check")}<span>${T(text)}</span>${link ? `<a>${link}</a>` : ""}</div>`;
  const dialog = (title, body, actions, icon) => `<div class="scrim"><div class="dialog" role="dialog" aria-modal="true" aria-label="${title}"><div class="dh">${icon ? ic(icon) : ""}${T(title)}</div><div class="db">${body}</div><div class="df">${actions}</div></div></div>`;

  // ------------------------------------------------------------------ layout pieces
  const box = (title, body, o = {}) => `<div class="box ${o.cls || ""}" style="${o.style || ""}">${title ? `<div class="bh">${T(title)}${o.actions ? `<span class="a">${o.actions}</span>` : ""}</div>` : ""}${o.raw ? body : `<div class="${o.nopad ? "" : "pad"}">${body}</div>`}</div>`;
  const figs = (items, cols) => `<div class="box figs" style="grid-template-columns:repeat(${cols || items.length},minmax(0,1fr))">${items.map(([k, v, s]) => `<div><span>${k}</span><b class="num">${v}</b>${s ? `<small>${T(s)}</small>` : ""}</div>`).join("")}</div>`;
  const kv = (items, cols = 3) => `<div class="kv" style="grid-template-columns:repeat(${cols},minmax(0,1fr))">${items.map(([k, v]) => `<div><div class="k">${k}</div><div>${T(v)}</div></div>`).join("")}</div>`;
  const tabs = (items, on = 0) => `<div class="tabs" role="tablist">${items.map((x, i) => `<span role="tab" aria-selected="${i === on}" class="${i === on ? "on" : ""}">${T(x)}</span>`).join("")}</div>`;
  const views = (items, on = 0, tools = "") => `<div class="views">${items.map((x, i) => `<span class="${i === on ? "on" : ""}">${T(x)}</span>`).join("")}<span style="color:var(--primary)">${ic("plus")} حفظ العرض</span><span class="tools">${tools}</span></div>`;
  const listTools = (o = {}) => `${o.cols !== false ? btn("الأعمدة", { icon: "columns", sm: 1 }) : ""}${btn("Excel", { icon: "export", sm: 1 })}${btn("PDF", { icon: "export", sm: 1 })}`;
  const hist = (items) => `<div class="hist">${items.map(([who, what, when, chg, v]) => `<div class="h ${v ? "v" : ""}"><i></i><b>${T(what)} — ${who}</b><span>${T(when)}${chg ? ` · ${chg}` : ""}</span></div>`).join("")}</div>`;
  const chg = (a, b) => `<span class="chg">${a} → ${b}</span>`;

  /**
   * Table. cols: [key, label, opts] where opts: {n:numeric, w:width, f:formatter(row)}. rows: objects or arrays.
   * o: {filters, check, sel:[indexes], foot:{key:value}, footLabel, live:index, voidKey, group}
   */
  function table(cols, rows, o = {}) {
    const val = (r, c, i) => (c[2] && c[2].f ? c[2].f(r, i) : (Array.isArray(r) ? r[c[0]] : r[c[0]]));
    const cell = (v, c) => (v === undefined || v === null ? "" : c[2] && c[2].f ? v : (c[2] && c[2].n ? N(v) : T(v)));
    const head = `<tr>${o.check ? '<th style="width:30px"><input type="checkbox" aria-label="تحديد الكل"></th>' : ""}${cols.map((c, i) => `<th class="${c[2] && c[2].n ? "n" : ""}" style="${c[2] && c[2].w ? `width:${c[2].w}` : ""}">${c[1]}${i === (o.sortCol ?? -1) ? ic("down", "s") : ""}</th>`).join("")}</tr>`;
    const flt = o.filters ? `<tr class="flt">${o.check ? "<td></td>" : ""}${cols.map((c) => `<td>${c[2] && c[2].nf ? "" : `<div class="input ${c[2] && c[2].n ? "num" : ""}">${(o.filters[c[0]] !== undefined) ? `<span>${T(o.filters[c[0]])}</span>` : `<span class="ph">${c[2] && c[2].n ? "≥" : "تصفية"}</span>`}</div>`}</td>`).join("")}</tr>` : "";
    const body = rows.map((r, i) => {
      if (r && r.__group) return `<tr class="grp"><td colspan="${cols.length + (o.check ? 1 : 0)}">${T(r.__group)}</td></tr>`;
      const cls = [(o.sel || []).includes(i) ? "sel" : "", o.live === i ? "live" : "", o.voidRow && o.voidRow(r) ? "void" : ""].join(" ");
      return `<tr class="${cls}">${o.check ? `<td><input type="checkbox" aria-label="تحديد" ${(o.sel || []).includes(i) ? "checked" : ""}></td>` : ""}${cols.map((c) => `<td class="${c[2] && c[2].n ? "n" : ""} ${c[2] && c[2].wrap ? "wrap" : ""}">${cell(val(r, c, i), c)}</td>`).join("")}</tr>`;
    }).join("");
    const foot = o.foot ? `<tfoot><tr>${o.check ? "<td></td>" : ""}${cols.map((c, i) => `<td class="${c[2] && c[2].n ? "n" : ""}">${i === 0 ? T(o.footLabel || "المجموع") : (o.foot[c[0]] !== undefined ? N(o.foot[c[0]]) : "")}</td>`).join("")}</tr></tfoot>` : "";
    return `<table class="grid ${o.cls || ""}"><thead>${head}${flt}</thead><tbody>${body}</tbody>${foot}</table>`;
  }
  const pager = (text, pages = "1 2 3 … 26") => `<div class="tbl-foot"><span>${T(text)}</span><span class="pg">${btn("", { icon: "left", sm: 1, aria: "السابق" }).replace("ic ", "ic ")}${pages.split(" ").map((p, i) => `<span class="${i === 0 ? "chip on" : "chip"}" style="height:24px;padding:0 8px">${N(p)}</span>`).join("")}<select aria-label="عدد الصفوف" style="height:24px;font:inherit;font-size:12px"><option>50 صفاً</option></select></span></div>`;
  /** A list block: saved views + table (with filter row) + pager. */
  const listBlock = (viewItems, tbl, pg, o = {}) => `${viewItems ? views(viewItems, o.view || 0, o.tools ?? listTools()) : ""}<div class="box fill" style="display:flex;flex-direction:column;overflow:hidden"><div class="fill">${tbl}</div>${pg ? pager(pg, o.pages) : ""}</div>`;

  // forms
  function field(label, value, o = {}) {
    const v = value === undefined || value === null || value === "" ? (o.ph ? `<span class="ph">${o.ph}</span>` : "") : (o.num ? N(value) : T(value));
    return `<div class="field" style="${o.span ? `grid-column:span ${o.span}` : ""}"><label>${label}${o.req ? ' <b aria-hidden="true">*</b>' : ""}</label>
      <div class="input ${o.num ? "num" : ""} ${o.focus ? "focus" : ""} ${o.err ? "bad" : ""} ${o.ro ? "ro" : ""} ${o.ta ? "ta" : ""} ${o.expand ? "expand" : ""}" ${o.err ? 'aria-invalid="true"' : ""} ${value ? `title="${String(value).replace(/"/g, "&quot;")}"` : ""}>${o.icon ? ic(o.icon) : ""}<span class="v">${v}</span>${o.dd ? `<span class="dd">${ic("down")}</span>` : ""}${o.cal ? `<span class="dd">${ic("calendar")}</span>` : ""}</div>
      ${o.err ? `<span class="help err">${ic("alert")}${T(o.err)}</span>` : o.help ? `<span class="help">${T(o.help)}</span>` : ""}${o.src ? `<span class="src">${ic("check")}${T(o.src)}</span>` : ""}</div>`;
  }
  const fg = (cols, inner, style = "") => `<div class="fg" style="grid-template-columns:repeat(${cols},minmax(0,1fr));${style}">${inner}</div>`;
  const chk = (label, on) => `<label class="chk"><input type="checkbox" ${on ? "checked" : ""}> ${T(label)}</label>`;
  const tog = (label, on, help) => `<div style="display:flex;gap:10px;align-items:flex-start;padding:7px 0;border-bottom:1px solid var(--border)"><span class="toggle ${on ? "on" : ""}" role="switch" aria-checked="${!!on}"></span><div><div>${T(label)}</div>${help ? `<div class="help">${T(help)}</div>` : ""}</div></div>`;
  const errsum = (items) => `<div class="errsum" role="alert"><b>يرجى تصحيح ${N(items.length)} حقول قبل الحفظ:</b><ul>${items.map((x) => `<li><a>${T(x)}</a></li>`).join("")}</ul></div>`;

  // page header
  const page = (h, body) => `<div class="ph"><div class="t">${h.crumbs ? `<div class="crumbs">${h.crumbs}</div>` : ""}<h3>${T(h.title)}${h.badges || ""}</h3></div><div class="act">${h.actions || ""}</div></div>${body}`;

  // states
  const empty = (e) => `<div class="empty"><div class="ei">${ic(e.icon || "file")}</div><h4>${T(e.title)}</h4><p>${T(e.text)}</p>${e.actions ? `<div class="acts">${e.actions}</div>` : ""}</div>`;
  const errstate = (e) => `<div class="empty errstate" role="alert"><div class="ei">${ic("alert")}</div><h4>${T(e.title || "تعذّر تحميل البيانات")}</h4><p>${T(e.text || "انقطع الاتصال بالخادم أثناء التحميل. بياناتك محفوظة؛ أعد المحاولة.")}</p><div class="acts">${btn("إعادة المحاولة", { pri: 1, icon: "refresh" })}</div><div class="help">رقم المتابعة: <span class="num">7F3A-21C9</span></div></div>`;
  function skeleton(kind) {
    const line = (w, h = 12) => `<div class="sk" style="width:${w};height:${h}px"></div>`;
    const rows = (n, cols) => Array.from({ length: n }, () => `<div style="display:grid;grid-template-columns:${cols};gap:14px;padding:9px 12px;border-bottom:1px solid var(--border)">${cols.split(" ").map(() => line("100%")).join("")}</div>`).join("");
    const k = { list: `<div class="box fill" aria-busy="true">${rows(14, "1fr 1fr 2fr 2fr 1fr 1fr 1fr")}</div>`,
      form: `<div class="box pad" aria-busy="true">${fg(4, Array.from({ length: 12 }, () => `<div style="display:flex;flex-direction:column;gap:6px">${line("40%", 10)}${line("100%", 30)}</div>`).join(""))}</div>`,
      report: `<div class="box pad" style="display:flex;flex-direction:column;gap:10px" aria-busy="true">${line("30%", 16)}${rows(12, "3fr 1fr 1fr 1fr")}</div>`,
      detail: `${`<div class="box pad" aria-busy="true">${line("25%", 18)}<div style="height:10px"></div>${fg(4, Array.from({ length: 8 }, () => line("90%")).join(""))}</div>`}<div class="box fill">${rows(8, "1fr 1fr 2fr 1fr 1fr")}</div>`,
      dash: `<div class="box" style="height:62px" aria-busy="true"><div class="sk" style="height:100%"></div></div><div class="cols" style="grid-template-columns:1.5fr 1fr;flex:1">${`<div class="box">${rows(8, "3fr 1fr 1fr")}</div>`}<div class="box">${rows(6, "2fr 1fr")}</div></div>` };
    return (k[kind] || k.list) + `<span class="muted" role="status" style="font-size:12.5px">جارٍ التحميل…</span>`;
  }

  // ------------------------------------------------------------------ sample data
  const D = {};
  D.trips = [
    ["TRP-10482", "2026-10-10", "أحمد صالح", "10-11111", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.000", "31.850", "6.000", "3.500", "191.100", "111.475", ["unbilled"]],
    ["TRP-10481", "2026-10-10", "سامي العبادي", "10-22222", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "31.500", "31.020", "6.000", "0.000", "186.120", "85.305", ["unbilled", "short"]],
    ["TRP-10480", "2026-10-09", "محمود الحويطات", "11-40312", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "33.200", "33.150", "5.250", "3.250", "174.038", "107.738", ["invoiced"]],
    ["TRP-10479", "2026-10-09", "خالد الزعبي", "12-77801", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "32.800", "32.790", "5.000", "3.250", "163.950", "106.568", ["unbilled", "breach"]],
    ["TRP-10478", "2026-10-09", "يوسف النعيمات", "10-55290", "الإسمنت الأردنية", "الحسا", "مستودعات الزرقاء", "كلنكر", "30.000", "29.980", "4.750", "3.900", "142.405", "116.922", ["invoiced", "manual"]],
    ["TRP-10477", "2026-10-08", "أحمد صالح", "10-11111", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.400", "32.300", "6.000", "3.500", "193.800", "113.050", ["invoiced", "settled"]],
    ["TRP-10476", "2026-10-08", "سامي العبادي", "10-22222", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "31.900", "31.880", "6.000", "0.000", "191.280", "87.670", ["invoiced"]],
    ["TRP-10475", "2026-10-08", "محمود الحويطات", "11-40312", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "33.000", "", "5.250", "3.250", "173.250", "107.250", ["void"]],
    ["TRP-10474", "2026-10-07", "خالد الزعبي", "12-77801", "الإسمنت الأردنية", "الحسا", "مستودعات الزرقاء", "كلنكر", "29.600", "29.590", "4.750", "3.900", "140.553", "115.401", ["invoiced"]],
    ["TRP-10473", "2026-10-07", "يوسف النعيمات", "10-55290", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.100", "31.950", "6.000", "3.500", "191.700", "111.825", ["unbilled", "review"]],
    ["TRP-10472", "2026-10-07", "أحمد صالح", "10-11111", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.000", "31.990", "6.000", "3.500", "191.940", "111.965", ["invoiced", "settled"]],
    ["TRP-10471", "2026-10-06", "سامي العبادي", "10-22222", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "31.700", "31.650", "5.250", "0.000", "166.163", "86.913", ["invoiced"]],
    ["TRP-10470", "2026-10-06", "محمود الحويطات", "11-40312", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "33.100", "33.060", "5.250", "3.250", "173.565", "107.445", ["invoiced"]],
    ["TRP-10469", "2026-10-06", "خالد الزعبي", "12-77801", "الإسمنت الأردنية", "الحسا", "مستودعات الزرقاء", "كلنكر", "30.200", "30.180", "4.750", "3.900", "143.355", "117.702", ["unbilled"]],
    ["TRP-10468", "2026-10-05", "يوسف النعيمات", "10-55290", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.300", "32.250", "6.000", "3.500", "193.500", "112.875", ["invoiced"]],
    ["TRP-10467", "2026-10-05", "أحمد صالح", "10-11111", "شركة البوتاس العربية", "غور الصافي", "ميناء العقبة", "بوتاس", "32.200", "32.150", "6.000", "3.500", "192.900", "112.525", ["invoiced", "settled"]],
    ["TRP-10466", "2026-10-05", "سامي العبادي", "10-22222", "مناجم الفوسفات الأردنية", "الشيدية", "ميناء العقبة", "فوسفات", "31.600", "31.590", "5.250", "0.000", "165.848", "86.873", ["invoiced"]],
  ];
  D.tripStatus = (t) => t[14].map(st).join(" ");
  D.tripCols = {
    rc: [0, "رقم السند", { n: 1 }], dt: [1, "التاريخ", { n: 1 }], drv: [2, "السائق"], pl: [3, "اللوحة", { n: 1 }], cu: [4, "العميل"], ls: [5, "موقع التحميل"], us: [6, "موقع التنزيل"],
    mat: [7, "المادة"], lw: [8, "محمّل (طن)", { n: 1 }], uw: [9, "منزّل (طن)", { n: 1 }], pr: [10, "السعر", { n: 1 }], rt: [11, "أجرة السائق", { n: 1 }], rev: [12, "الإيراد", { n: 1 }],
    wg: [13, "الأجر / التكلفة", { n: 1 }], st: [14, "الحالة", { f: D.tripStatus, nf: 1 }],
  };
  D.tc = (...keys) => keys.map((k) => D.tripCols[k]);
  D.customers = [["شركة البوتاس العربية", "200014587", "0791234500", "30", "18,412.600", "61", "active"], ["مناجم الفوسفات الأردنية", "200003311", "0795553100", "45", "22,904.150", "18", "active"], ["الإسمنت الأردنية", "200118020", "0786611220", "30", "6,210.000", "7", "active"], ["مصانع الأسمدة الوطنية", "200450117", "0772204410", "60", "0.000", "0", "archived"]];
  D.drivers = [["أحمد صالح", "9871234560", "10-11111", "فولفو FH · 32 t", "الشركة", "", "0791234567", "850.000", ["active", "w-ins"]], ["سامي العبادي", "9871234561", "10-22222", "مرسيدس أكتروس · 32 t", "مستأجرة", "النور للنقل", "0777123456", "—", ["active"]], ["محمود الحويطات", "9762210034", "11-40312", "مان TGX · 33 t", "الشركة", "", "0795541123", "1,212.300", ["active", "x-lic"]], ["خالد الزعبي", "9810007712", "12-77801", "سكانيا R450 · 32 t", "مالك سائق", "", "0789004512", "406.950", ["active"]], ["يوسف النعيمات", "9905512380", "10-55290", "فولفو FM · 30 t", "الشركة", "", "0796650091", "0.000", ["active"]]];
  D.user = "رنا الخطيب";
  D.hist = [[D.user, "تعديل", "اليوم 10:42", "الوزن المنزّل " + chg("31.700", "31.850")], ["سارة حداد", "أضيفت إلى الفاتورة INV-02211", "2026-10-09 16:05"], ["سارة حداد", "تسوية STL-00318", "2026-10-08 12:30"], [D.user, "إنشاء", "2026-10-08 09:12"]];

  // ------------------------------------------------------------------ navigation and shell
  const NAV = [
    { g: "لوحة المعلومات", icon: "home", id: "SCR-07" },
    { g: "قائمة الإعداد", icon: "check", id: "SCR-06" },
    { g: "العمليات", icon: "truck", items: [["الرحلات", "SCR-10"], ["رحلة جديدة", "SCR-11"], ["استيراد الرحلات", "SCR-12"], ["متابعة النقص", "SCR-13"]] },
    { g: "الفوترة والنقد", icon: "file", items: [["الفواتير", "SCR-20"], ["فاتورة رحلات جديدة", "SCR-21"], ["فاتورة يدوية", "SCR-22"], ["عرض فاتورة", "SCR-23"], ["الإشعارات الدائنة", "SCR-24"], ["الفوترة الإلكترونية", "SCR-25"], ["سندات القبض", "SCR-26"], ["حسابات السائقين", "SCR-27"], ["حسابات الشركاء", "SCR-28"]] },
    { g: "المحاسبة", icon: "book", items: [["دليل الحسابات", "SCR-30"], ["القيود اليومية", "SCR-31"], ["قيد جديد", "SCR-32"], ["استيراد القيود", "SCR-33"], ["القيود الآلية والمعلّقة", "SCR-34"], ["الأستاذ العام", "SCR-35"], ["ميزان المراجعة", "SCR-36"], ["الأرباح والخسائر", "SCR-37"], ["الميزانية العمومية", "SCR-38"], ["كشف حساب", "SCR-39"], ["الذمم الدائنة", "SCR-40"], ["الذمم المدينة", "SCR-41"], ["البنوك والصناديق", "SCR-42"], ["مراكز التكلفة", "SCR-43"], ["الفترات", "SCR-44"], ["منطقة الصيانة", "SCR-45"]] },
    { g: "التقارير", icon: "chart", items: [["التقارير التشغيلية", "SCR-50"], ["التقارير المالية والأعمار", "SCR-51"], ["منشئ التقارير", "SCR-52"], ["التقارير المحفوظة", "SCR-53"]] },
    { g: "البيانات الأساسية", icon: "db", items: [["السائقون والشاحنات", "SCR-60"], ["العملاء", "SCR-61"], ["الشركاء", "SCR-62"], ["المواد", "SCR-63"], ["مواقع التحميل", "SCR-64"], ["مواقع التنزيل", "SCR-65"], ["أنواع الملكية", "SCR-66"], ["التعرفة", "SCR-67"], ["الأسعار الشهرية", "SCR-68"]] },
    { g: "الإعدادات", icon: "gear", items: [["إعدادات الشركة", "SCR-70"], ["ربط الحسابات", "SCR-71"], ["الترقيم", "SCR-72"], ["الفوترة الإلكترونية", "SCR-73"], ["مصمم نموذج الفاتورة", "SCR-74"], ["تهيئة الحقول", "SCR-75"], ["المستخدمون والأدوار", "SCR-76"], ["سجل التدقيق", "SCR-77"]] },
    { g: "المشترك", icon: "building", items: [["الاشتراك والفوترة", "SCR-80"], ["الشركات", "SCR-81"], ["تصدير البيانات", "SCR-82"], ["طلبات وصول الدعم", "SCR-83"]] },
  ];
  const PNAV = [["المشتركون", "SCR-90", "users"], ["الخطط", "SCR-91", "layers"], ["وصول الدعم", "SCR-92", "shield"], ["الإعلانات", "SCR-93", "megaphone"], ["صحة المنصة", "SCR-94", "activity"]];

  function sideNav(cur) {
    return NAV.map((n) => {
      if (!n.items) return `<div class="g ${n.id === cur ? "cur" : ""}" data-go="${n.id}">${ic(n.icon)}${n.g}</div>`;
      const open = n.items.some((x) => x[1] === cur);
      return `<div class="g ${open ? "open" : ""}" data-go="${n.items[0][1]}">${ic(n.icon)}${n.g}${ic(open ? "down" : "left", "ch")}</div>${open ? n.items.map(([l, id]) => `<div class="it ${id === cur ? "cur" : ""}" data-go="${id}">${l}</div>`).join("") : ""}`;
    }).join("");
  }
  function shell(cur, content, o = {}) {
    const notif = o.notifCount ?? 4;
    return `<div class="shell"><aside class="side" aria-label="القائمة الرئيسية"><div class="brand"><img src="${LOGO}" alt="">دايركت سيرفس للنقل</div><nav>${sideNav(cur)}</nav><div class="foot">الخطة: احترافية · ${N("18/25")} مستخدماً</div></aside>
      <div class="main"><div class="top">${btn("الشركة العربية للنقل", { icon: "building" }).replace("</button>", ` ${ic("down")}</button>`)}
        <div class="cmdk" role="search">${ic("search")}<span>ابحث برقم سند أو فاتورة أو اسم سائق…</span><span class="kbd" style="margin-inline-start:auto">Ctrl K</span></div>
        <button class="btn ghost bell" aria-label="الإشعارات (${notif})">${ic("bell")}${notif ? `<span class="dot">${notif}</span>` : ""}</button>
        <button class="btn ghost">${ic("user")}${D.user} ${ic("down")}</button></div>
        <div class="content">${o.banner || ""}${content}</div></div></div>${o.overlay || ""}`;
  }
  function pshell(cur, content, o = {}) {
    return `<div class="shell provider"><aside class="side" aria-label="قائمة مزوّد الخدمة"><div class="brand"><img src="${LOGO}" alt="">لوحة مزوّد الخدمة</div><nav>${PNAV.map(([l, id, i]) => `<div class="g ${id === cur ? "cur" : ""}" data-go="${id}">${ic(i)}${l}</div>`).join("")}</nav><div class="foot">موظفو المزوّد فقط · التحقق بخطوتين إلزامي</div></aside>
      <div class="main"><div class="top"><span class="badge b-mute">${ic("shield")}بيئة الإنتاج</span><div class="cmdk">${ic("search")}<span>ابحث عن مشترك…</span><span class="kbd" style="margin-inline-start:auto">Ctrl K</span></div><button class="btn ghost">${ic("user")}عمر المصري · الدعم ${ic("down")}</button></div>
      <div class="content">${o.banner || ""}${content}</div></div></div>${o.overlay || ""}`;
  }

  // ------------------------------------------------------------------ registry
  const REG = [];
  const BYID = {};
  const GROUPS = ["Shell and start", "Operations", "Billing & cash", "Accounting", "Reports", "Master data", "Settings", "Tenant", "Provider console"];
  /**
   * s = { id, group, ar, en, url, kind: list|form|report|detail|dash|auth|print, head:{crumbs,title,actions,badges},
   *       render(state, variant) → content, variants:[[key,label]], empty:{icon,title,text,actions}, error:{title,text} | errorRender(variant),
   *       success: text | successRender(variant), bare:true (no shell), provider:true, notes }
   */
  function add(s) { REG.push(s); BYID[s.id] = s; }

  function compose(s, state, variant) {
    const wrap = (inner, o) => (s.bare ? inner : (s.provider ? pshell : shell)(s.nav || s.id, inner, o));
    const head = typeof s.head === "function" ? s.head(variant) : s.head;
    if (state === "loading") {
      if (s.loadingRender) return wrap(s.loadingRender(variant));
      if (s.kind === "auth") {
        // Auth forms: the submit is in progress; fields are disabled and the button shows progress.
        const r = s.render("default", variant);
        return wrap((typeof r === "string" ? r : r.html) + `<div class="scrim" style="background:rgba(255,255,255,.55);place-items:center end;padding-inline-end:220px" aria-busy="true"><div style="background:var(--surface);border:1px solid var(--border);border-radius:4px;padding:10px 16px;display:flex;gap:10px;align-items:center"><div class="sk" style="width:18px;height:18px;border-radius:50%"></div><span role="status">جارٍ التحقق…</span></div></div>`);
      }
      return wrap(page({ ...head, actions: "" }, skeleton(s.kind)));
    }
    if (state === "empty") {
      if (s.emptyRender) return wrap(s.emptyRender(variant));
      if (!s.empty || !s.empty.title) {
        // Screens that always hold data (settings, plans, health): the "empty" state is the first visit with defaults.
        const r = s.render("default", variant);
        const html = typeof r === "string" ? r : r.html;
        return wrap(html.replace('<div class="ph">', `${banner("info", "أول زيارة: القيم الافتراضية معروضة ولم يُعدّل شيء بعد.")}<div class="ph">`));
      }
      return wrap(page(head, (s.emptyLead ? s.emptyLead : "") + `<div class="box fill" style="display:flex">${empty(s.empty || { title: "لا توجد بيانات بعد", text: "" })}</div>`));
    }
    if (state === "error") {
      if (s.errorRender) { const r = s.errorRender(variant); return typeof r === "string" ? wrap(r) : wrap(r.html, r.o); }
      return wrap(page(head, `<div class="box fill" style="display:flex">${errstate(s.error || {})}</div>`));
    }
    const r = s.render(state, variant);
    const html = typeof r === "string" ? r : r.html;
    const o = typeof r === "string" ? {} : r.o || {};
    if (state === "success") {
      const t = s.successRender ? s.successRender(variant) : toast(s.success || "تم الحفظ.");
      return wrap(html, { ...o, overlay: (o.overlay || "") + t });
    }
    return wrap(html, o);
  }

  // ------------------------------------------------------------------ review controls
  const STATES = [["default", "Default"], ["loading", "Loading"], ["empty", "Empty"], ["error", "Error"], ["success", "Success"]];
  let cur = "SCR-10", state = "default", variant = null, density = "compact";
  function load() { try { const v = JSON.parse(localStorage.getItem("ds-step6") || "{}"); if (BYID[v.cur]) cur = v.cur; if (v.state) state = v.state; if (v.density) density = v.density; } catch (e) { /* storage unavailable */ } }
  function save() { try { localStorage.setItem("ds-step6", JSON.stringify({ cur, state, density })); } catch (e) { /* storage unavailable */ } }

  function render() {
    const s = BYID[cur];
    if (!s.variants || !s.variants.some((v) => v[0] === variant)) variant = s.variants ? s.variants[0][0] : null;
    const sel = document.getElementById("sel-screen");
    sel.value = cur;
    document.getElementById("seg-state").innerHTML = STATES.map(([k, l]) => `<button data-v="${k}" aria-pressed="${k === state}">${l}</button>`).join("");
    const lv = document.getElementById("lab-variant");
    lv.hidden = !s.variants;
    document.getElementById("seg-variant").innerHTML = (s.variants || []).map(([k, l]) => `<button data-v="${k}" aria-pressed="${k === variant}">${l}</button>`).join("");
    document.querySelectorAll("#seg-density button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.v === density)));
    const scr = document.getElementById("screen");
    scr.innerHTML = `<div class="app ${density === "comfy" ? "comfy" : ""}">${compose(s, state, variant)}</div>`;
    isolate(scr);
    document.getElementById("url").textContent = "app.directservice.jo/" + (s.url || "c/7kQ2m9/" + s.id.toLowerCase());
    document.getElementById("meta").innerHTML = `<span><b>${s.id}</b> · ${s.en} · <span lang="ar" dir="rtl">${s.ar}</span></span><span>${s.group}</span>${s.reqs ? `<span>Covers: ${s.reqs}</span>` : ""}`;
    document.getElementById("notes").innerHTML = `<b>${STATES.find((x) => x[0] === state)[1]} state.</b> ${(s.notes && s.notes[state]) || (s.notes && s.notes.default) || ""}`;
    scr.querySelectorAll("[data-go]").forEach((el) => el.addEventListener("click", () => { if (BYID[el.dataset.go]) { cur = el.dataset.go; state = "default"; save(); render(); } }));
  }

  /**
   * Bidi safety net over the rendered screen: every text node that still holds a left-to-right run (code, number, date,
   * Latin word) outside an isolate gets its runs wrapped, the same rule the Blazor components apply with &lt;bdi&gt;.
   */
  function isolate(root) {
    const esc = (t) => t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    const nodes = [];
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      if (/[A-Za-z0-9]/.test(n.nodeValue) && !n.parentElement.closest(".num,.kbd,svg,style,script,select,option,textarea")) nodes.push(n);
    }
    for (const n of nodes) {
      const span = document.createElement("span");
      span.innerHTML = esc(n.nodeValue).replace(RUN, '<span class="num">$1</span>');
      n.replaceWith(...span.childNodes);
    }
  }

  function boot() {
    load();
    const sel = document.getElementById("sel-screen");
    sel.innerHTML = GROUPS.map((g) => `<optgroup label="${g}">${REG.filter((s) => s.group === g).map((s) => `<option value="${s.id}">${s.id} · ${s.en}</option>`).join("")}</optgroup>`).join("");
    sel.addEventListener("change", () => { cur = sel.value; state = "default"; save(); render(); });
    const order = GROUPS.flatMap((g) => REG.filter((s) => s.group === g).map((s) => s.id));
    document.getElementById("prev").addEventListener("click", () => { cur = order[(order.indexOf(cur) - 1 + order.length) % order.length]; save(); render(); });
    document.getElementById("next").addEventListener("click", () => { cur = order[(order.indexOf(cur) + 1) % order.length]; save(); render(); });
    document.getElementById("seg-state").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) { state = b.dataset.v; save(); render(); } });
    document.getElementById("seg-variant").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) { variant = b.dataset.v; render(); } });
    document.getElementById("seg-density").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) { density = b.dataset.v; save(); render(); } });
    document.getElementById("index").innerHTML = GROUPS.map((g) => `<h3>${g}</h3>${REG.filter((s) => s.group === g).map((s) => `<button data-id="${s.id}">${s.id} · ${s.en}</button>`).join("")}`).join("");
    document.getElementById("index").addEventListener("click", (e) => { const b = e.target.closest("button"); if (b) { cur = b.dataset.id; state = "default"; save(); render(); document.querySelector(".ctl").scrollIntoView(); } });
    document.addEventListener("keydown", (e) => { if (e.target.tagName === "SELECT") return; if (e.key === "ArrowRight" && e.altKey) document.getElementById("prev").click(); if (e.key === "ArrowLeft" && e.altKey) document.getElementById("next").click(); });
    render();
  }

  return { ic, N, T, badge, st, btn, banner, toast, dialog, box, figs, kv, tabs, views, listTools, hist, chg, table, pager, listBlock, field, fg, chk, tog, errsum, page, empty, errstate, skeleton, D, add, boot, LOGO, shell };
})();
