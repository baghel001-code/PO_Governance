// VMM Assistant chat panel helpers: Enter sends (Shift+Enter = newline), keep the conversation scrolled to the end.
export function attach(textarea, sendButton) {
    if (!textarea || textarea._aicAttached) return;
    textarea._aicAttached = true;
    textarea.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            // let the pending input event reach the server before the click
            setTimeout(function () { if (sendButton && !sendButton.disabled) sendButton.click(); }, 0);
        }
    });
}

export function scrollToEnd(el) {
    if (el) el.scrollTop = el.scrollHeight;
}

export function focus(el) {
    if (el) el.focus();
}
