# VMM PO Governance v1.1.6 — reference page (Python / browser version)

The .NET rebuild was ported from v1.1.4 (`legacy/python-app`, kept unchanged). v1.1.6 (4–5 Nov 2026) changed
single concept rules and screens; this folder is the source of truth for those changes:

| File | What it is |
|---|---|
| `RELEASE-NOTES-v1.1.6.txt` | The v1.1.6 release notes (items 1–15) |
| `patches/*.py` | The exact v1.1.6 edits of the page, unchanged from the Python package (`dev/patches`); the v1.1.7 MRA patch (31) is not here |
| `build.py` | `python3 legacy/v1.1.6/build.py` applies the patches to `legacy/python-app/app/index.html` and writes `app/index.html` |
| `app/index.html` | The built v1.1.6 page (the parity harnesses `tests/parity/{tool1,tool2,recommendation}` run it) |
| `app/explain.js` | The "How was this number worked out?" side panel + Excel with formulas (single concept) and the BOBs sibling list |
| `app/shared.js`, `app/users.js` | v1.1.4 files + the v1.1.6 lines (late PO cut-off day is a shared parameter; the approver sees "Why introduce this colour?") |

Ported to .NET: see the commit history ("v1.1.6 …"). Not ported on purpose: patch 26 (a browser-only selector bug).
