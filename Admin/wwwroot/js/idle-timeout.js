// Tracks user activity in the browser and reports it to IdleTimeoutMonitor.razor (QPS idle-timeout.js).
// Loaded as an ES module via IJSRuntime "import" (no <script> tag needed).

const STORAGE_KEY = 'pogov.lastActivity';
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'scroll', 'touchstart'];

export function start(dotNetRef, options) {
    const reportThrottleMs = options.reportThrottleMs;
    const keepAliveUrl = options.keepAliveUrl;
    const keepAliveIntervalMs = options.keepAliveIntervalMs;

    let disposed = false;
    let lastReport = 0;
    let lastBroadcast = 0;
    let lastKeepAlive = Date.now(); // the page load itself was an HTTP request

    const reportToServer = (now) => {
        if (now - lastReport < reportThrottleMs) return;
        lastReport = now;
        dotNetRef.invokeMethodAsync('ReportActivity').catch(() => { /* circuit gone */ });
    };

    // Keeps the auth cookie (sliding) and the POGovApi session alive while the user is active.
    const keepAlive = (now) => {
        if (!keepAliveUrl || now - lastKeepAlive < keepAliveIntervalMs) return;
        lastKeepAlive = now;
        fetch(keepAliveUrl, { method: 'POST', credentials: 'same-origin', redirect: 'manual', cache: 'no-store' })
            .catch(() => { /* offline; the next activity retries */ });
    };

    // Share activity with other tabs so working in one tab doesn't log out the others.
    const broadcast = (now) => {
        if (now - lastBroadcast < reportThrottleMs) return;
        lastBroadcast = now;
        try { localStorage.setItem(STORAGE_KEY, String(now)); } catch { /* storage blocked */ }
    };

    const onActivity = () => {
        if (disposed) return;
        const now = Date.now();
        reportToServer(now);
        keepAlive(now);
        broadcast(now);
    };

    const onStorage = (e) => {
        if (disposed || e.key !== STORAGE_KEY) return;
        reportToServer(Date.now());
    };

    ACTIVITY_EVENTS.forEach(evt => document.addEventListener(evt, onActivity, { passive: true, capture: true }));
    window.addEventListener('storage', onStorage);

    return {
        dispose() {
            disposed = true;
            ACTIVITY_EVENTS.forEach(evt => document.removeEventListener(evt, onActivity, { capture: true }));
            window.removeEventListener('storage', onStorage);
        }
    };
}