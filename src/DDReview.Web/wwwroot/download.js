// Hands a base64 file to the browser as a download (used by the Excel buttons).
window.ddDownload = function (name, base64, type) {
  const bin = atob(base64); const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  const url = URL.createObjectURL(new Blob([bytes], { type: type || 'application/octet-stream' }));
  const a = document.createElement('a'); a.href = url; a.download = name; document.body.appendChild(a); a.click();
  setTimeout(() => { URL.revokeObjectURL(url); a.remove(); }, 1000);
};

// Close any open dropdown-style multi-select when the click lands outside it.
document.addEventListener("click", function (e) {
    document.querySelectorAll("details.ms[open]").forEach(function (d) { if (!d.contains(e.target)) d.removeAttribute("open"); });
});

// Full screen / normal screen buttons in the header. The page shows one or the other by the "fullscreen" class on <html>,
// so it stays right however the header is re-rendered. Escape (or F11 from the browser) also leaves full screen and is picked up here.
document.addEventListener("click", function (e) {
    if (!e.target.closest || !e.target.closest("[data-fullscreen]")) return;
    try {
        if (document.fullscreenElement) document.exitFullscreen();
        else document.documentElement.requestFullscreen();
    } catch (err) { /* browser refused; nothing to do */ }
});
// Browsers on tablets switch pinch-zoom off in full screen, so in full screen the page scales itself to fit the width of what is on it
// (CSS zoom on <html>), and follows the window turning, the page changing and a wider report arriving. Leaving full screen puts the zoom back.
(function () {
    var timer = 0, working = false;
    function isFull() { return !!document.fullscreenElement; }
    function fit() {
        if (working) return;
        working = true;
        var root = document.documentElement;
        try {
            if (!isFull()) { root.style.zoom = ""; return; }
            root.style.zoom = "";                                  // measure the page at normal size
            var need = root.scrollWidth, have = window.innerWidth;
            root.style.zoom = need > have + 1 ? String(Math.max(0.25, have / need)) : "";
        } finally { working = false; }
    }
    function later() { clearTimeout(timer); timer = setTimeout(fit, 150); }
    document.addEventListener("fullscreenchange", function () {
        document.documentElement.classList.toggle("fullscreen", isFull());
        fit(); later();
    });
    window.addEventListener("resize", function () { if (isFull()) later(); });
    window.addEventListener("orientationchange", function () { if (isFull()) later(); });
    new MutationObserver(function () { if (isFull()) later(); }).observe(document.documentElement, { childList: true, subtree: true });
})();
