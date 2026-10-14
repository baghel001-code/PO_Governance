// How-to video page — the small script of index.html view-cat-video (speed buttons remembered in localStorage
// 'hv-speed', chapter seek, highlight of the chapter being played).
export function init(video, chaptersHost, chapters) {
    if (!video) return 1;
    let cur = 1;
    try { cur = parseFloat(localStorage.getItem('hv-speed')) || 1; } catch (e) { }
    video._hvSpeed = cur;
    video._hvChapters = Array.isArray(chapters) ? chapters : [];
    video.addEventListener('loadedmetadata', function () { video.playbackRate = video._hvSpeed; });
    video.addEventListener('play', function () { video.playbackRate = video._hvSpeed; });
    video.addEventListener('timeupdate', function () { mark(video, chaptersHost); });
    return cur;
}

export function setSpeed(video, s) {
    if (!video) return;
    video._hvSpeed = s;
    video.playbackRate = s;
    try { localStorage.setItem('hv-speed', String(s)); } catch (e) { }
}

export function seek(video, t) {
    if (!video) return;
    video.currentTime = t;
    video.playbackRate = video._hvSpeed || 1;
    const p = video.play();
    if (p && p.catch) p.catch(function () { });
}

function mark(video, host) {
    const chs = video._hvChapters || [];
    if (!host || !chs.length) return;
    const t = video.currentTime;
    let k = 0;
    chs.forEach(function (c, i) { if (t >= c.t - 0.2) k = i; });
    [].forEach.call(host.querySelectorAll('.hv-ch'), function (b, i) { b.classList.toggle('on', i === k); });
}

export function pause(video) {
    if (video && !video.paused) video.pause();
}
