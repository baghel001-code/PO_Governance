"""Builds legacy/v1.1.6/app/index.html = the v1.1.4 page (legacy/python-app/app/index.html) + the v1.1.6
page patches in patches/ (taken unchanged from the Python v1.1.6/v1.1.7 package dev/patches, without the
v1.1.7 MRA patch 31). Every edit must match exactly the expected number of times.

    python3 legacy/v1.1.6/build.py
"""
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "python-app", "app", "index.html")
OUT = os.path.join(HERE, "app", "index.html")
html = open(SRC, encoding="utf-8").read()
log = []


def rep(old, new, count=1, label=""):
    global html
    n = html.count(old)
    if n != count:
        sys.exit("PATCH FAILED [%s]: expected %d match(es), found %d\n  %r" % (label, count, n, old[:160]))
    html = html.replace(old, new)
    log.append(label)


for _pf in sorted(glob.glob(os.path.join(HERE, "patches", "*.py"))):
    exec(compile(open(_pf, encoding="utf-8").read(), _pf, "exec"), globals())
open(OUT, "w", encoding="utf-8").write(html)
print("OK - %d patches applied -> %s (%d bytes)" % (len(log), OUT, len(html.encode("utf-8"))))
