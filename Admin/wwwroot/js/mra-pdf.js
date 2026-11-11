// MRA panel / MRA verdict page: "Download PDF" saves the panel content (#data-pdf-target) as an A4 PDF.
// html2pdf.js 0.10.2 (MIT, lib/html2pdf) is loaded on the first click only - no internet needed.
(function () {
    var loading = null;
    function lib() {
        if (window.html2pdf) return Promise.resolve(window.html2pdf);
        if (!loading) {
            loading = new Promise(function (ok, fail) {
                var s = document.createElement('script');
                s.src = new URL('lib/html2pdf/html2pdf.bundle.min.js', document.baseURI).href;
                s.onload = function () { ok(window.html2pdf); };
                s.onerror = function () { loading = null; fail(new Error('PDF library could not be loaded')); };
                document.head.appendChild(s);
            });
        }
        return loading;
    }
    window.pogovMraPdf = function (btn) {
        var el = document.getElementById(btn.getAttribute('data-pdf-target'));
        if (!el) return;
        var name = btn.getAttribute('data-pdf-name') || 'MRA.pdf';
        var label = btn.textContent;
        btn.disabled = true; btn.textContent = 'Preparing PDF…';
        // html2canvas offsets the capture by the page scroll: with the MRA Queues list scrolled down the PDF came out
        // shifted down and cut off. Capture from the top of the page, then put the scroll back.
        var sx = window.scrollX, sy = window.scrollY;
        lib().then(function (html2pdf) {
            window.scrollTo(0, 0);
            return html2pdf().set({
                margin: [8, 8, 10, 8],
                filename: name,
                image: { type: 'jpeg', quality: 0.95 },
                html2canvas: { scale: 2, backgroundColor: '#ffffff', useCORS: true, scrollX: 0, scrollY: 0,
                               windowWidth: document.documentElement.clientWidth, windowHeight: Math.max(window.innerHeight, el.scrollHeight + 100) },
                jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' },
                pagebreak: { mode: ['css', 'legacy'], avoid: ['tr', '.mq-pbox'] }
            }).from(el).save();
        }).catch(function (e) {
            alert('The PDF could not be made: ' + (e && e.message ? e.message : e));
        }).then(function () { window.scrollTo(sx, sy); btn.disabled = false; btn.textContent = label; });
    };
})();
