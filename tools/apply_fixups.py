#!/usr/bin/env python3
"""Apply config/fixups.json to the recompiler output in generated/.

RecompOne patches work on whole functions; these are single-statement edits
inside a function too large to replace. Each fixup replaces the first `find`
after its `after` anchor. The kit launcher (tools/Launcher) does the same.
"""
import json, os, sys
root = sys.argv[1]
fixups = json.load(open(os.path.join(root, "config", "fixups.json")))
for f in fixups:
    path = os.path.join(root, "generated", f["file"])
    text = open(path, newline="").read()
    if f["replace"] in text:
        continue
    anchor = text.find(f["after"])
    at = text.find(f["find"], anchor) if anchor >= 0 else -1
    if at < 0 or at - anchor > 200:
        sys.exit(f"[fixup] not found in {f['file']}: {f['after']} ... {f['find']}")
    text = text[:at] + f["replace"] + text[at + len(f["find"]):]
    open(path, "w", newline="").write(text)
    print(f"[fixup] {f['file']} {f['after']}")
