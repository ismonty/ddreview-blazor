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
