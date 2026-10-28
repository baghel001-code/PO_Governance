// QPS wwwroot/js/auth.js.

// Posts a form as a normal browser request (full page load). form.submit() skips submit
// handlers, so Blazor's enhanced navigation can't turn it into a fetch. The response of that
// real HTTP request is what sets or clears the auth cookie.
//
// First fetches a fresh antiforgery token from tokenUrl and puts it in the form: Blazor's
// <AntiforgeryToken /> is empty on interactive pages that were not prerendered.
export async function submitForm(id, tokenUrl) {
    const form = document.getElementById(id);
    if (!form) return;

    const response = await fetch(tokenUrl, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) throw new Error(`Antiforgery token request failed (${response.status})`);
    const { field, token } = await response.json();
    if (!field || !token) throw new Error('Antiforgery token response was empty');

    let input = form.querySelector(`input[name="${field}"]`);
    if (!input) {
        input = document.createElement('input');
        input.type = 'hidden';
        input.name = field;
        form.appendChild(input);
    }
    input.value = token;
    form.submit();
}