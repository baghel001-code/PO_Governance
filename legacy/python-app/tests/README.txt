End-to-end tests (Python 3 + Playwright with Chromium).
  accept.py   - full acceptance run used before every release: fresh server, real data zip + SCR file,
                buyer / category head / admin flows (17 checks). Edit the paths and BASE at the top.
  fixtest.py  - checks for the v1.1.4 fixes (server date, own-approval block, Clear All, visibility,
                user edits, backup locking, parameter sync, cookie refresh).
  others      - earlier focused tests (auth, history, conflict, ids, AI mock, update).
Start a test server first, e.g.:  VMM_PORT=8796 VMM_DATA_DIR=/tmp/testdata python server.py
