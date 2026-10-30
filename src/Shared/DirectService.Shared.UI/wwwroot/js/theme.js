// Theme before the first paint (DT-244), loaded without defer in the <head> of App.razor (the CSP allows no inline script): this device's
// last choice ("dst.theme": light, dark or system = follow the device) → <html data-theme-choice> and the resolved data-theme. Applied
// again when the device's setting changes and when enhanced navigation replaces the <html> attributes. ui.js applyTheme changes it.
(function () {
  var d = document.documentElement, q = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;
  function stored() { try { return localStorage.getItem("dst.theme"); } catch (e) { return null; } }
  function apply() {
    var c = d.getAttribute("data-theme-choice") || stored();
    if (c !== "light" && c !== "dark") c = "system";
    var t = c === "dark" || (c === "system" && q && q.matches) ? "dark" : "light";
    if (d.getAttribute("data-theme-choice") !== c) d.setAttribute("data-theme-choice", c);
    if (d.getAttribute("data-theme") !== t) d.setAttribute("data-theme", t);
  }
  apply();
  if (q) { try { q.addEventListener("change", apply); } catch (e) { q.addListener(apply); } }
  new MutationObserver(apply).observe(d, { attributes: true, attributeFilter: ["data-theme", "data-theme-choice"] });
})();
