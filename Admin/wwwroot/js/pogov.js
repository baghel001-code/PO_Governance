// Small browser helpers for the PO Governance portal.
window.pogov = {
  // Save a file streamed from .NET (Excel exports).
  downloadFile: async function (fileName, contentType, streamRef) {
    const buf = await streamRef.arrayBuffer();
    const blob = new Blob([buf], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = fileName || 'download';
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  },
  scrollToTop: function () { window.scrollTo({ top: 0, behavior: 'smooth' }); }
};
