#!/usr/bin/env python3
"""Repair function boundaries in config/funcmaps after FuncMapGen.

RecompOne's linear sweep sometimes starts a function a word or two early, on
the tail of a string that sits right before it (JM2SHELL's func at 0x800D4790
was mapped as 0x800D478C). The real entry is then missing from the dispatch
table and the runtime skips the call. This pass collects every address that
code really calls -- JAL targets inside known functions, plus data words that
point at a stack-frame prologue -- and lui/addiu pairs that build such a pointer -- and makes each one a function start,
splitting or adding map entries as needed.

usage: fix_funcmaps.py <cue> <config dir>
"""
import json, os, re, struct, sys

SHELL_BASE, TRACK_BASE = 0x800C10B8, 0x801020B8

def read_disc(cue):
    d = os.path.dirname(cue)
    name = re.search(r'FILE "([^"]+)"', open(cue).read()).group(1)
    f = open(os.path.join(d, name), "rb")
    def sec(n):
        f.seek(n * 2352 + 24); return f.read(2048)
    def rd(lba, size):
        return b"".join(sec(lba + i) for i in range((size + 2047) // 2048))[:size]
    pvd = sec(16)
    files = {}
    def walk(lba, size, pre):
        data = rd(lba, size); i = 0
        while i < len(data):
            l = data[i]
            if l == 0:
                i = (i // 2048 + 1) * 2048; continue
            e = data[i:i + l]; name = e[33:33 + e[32]]
            el, es = struct.unpack("<I", e[2:6])[0], struct.unpack("<I", e[10:14])[0]
            if name not in (b"\0", b"\1"):
                n = name.decode().split(";")[0]
                if e[25] & 2: walk(el, es, pre + n + "/")
                else: files[pre + n] = (el, es)
            i += l
    walk(struct.unpack("<I", pvd[158:162])[0], struct.unpack("<I", pvd[166:170])[0], "")
    return files, rd

def main():
    cue, cfg = sys.argv[1], sys.argv[2]
    files, rd = read_disc(cue)
    mods = {}
    exe = rd(*files["SCUS_941.67"])
    mods["main"] = (struct.unpack("<I", exe[0x18:0x1C])[0], exe[0x800:])
    for path, (lba, size) in files.items():
        if path.startswith("BIN/") and path.endswith(".BIN"):
            stem = path[4:-4].lower()
            mods[stem] = (SHELL_BASE if stem == "jm2shell" else TRACK_BASE, rd(lba, size))

    maps = {n: json.load(open(os.path.join(cfg, "funcmaps", n + ".json"))) for n in mods}
    words = {n: struct.unpack("<%dI" % (len(b) // 4), b[:len(b) // 4 * 4]) for n, (_, b) in mods.items()}

    def word_at(n, addr):
        base = mods[n][0]; i = (addr - base) // 4
        return words[n][i] if 0 <= i < len(words[n]) and addr % 4 == 0 else None

    def is_prologue(w):
        return w is not None and (w & 0xFFFF8000) == 0x27BD8000

    # which modules can be in memory together: main + shell, or main + one track
    def visible(n):
        return [n] if n == "main" else ["main", n]

    targets = {n: set() for n in mods}
    for n in mods:
        base = mods[n][0]
        funcs = [(int(f["address"], 16), f["size"]) for f in maps[n]["functions"]]
        incode = set()
        for a, s in funcs:
            incode.update(range(a, a + s, 4))
        for i, w in enumerate(words[n]):
            addr = base + i * 4
            cands = []
            if addr in incode and w >> 26 == 3:
                cands.append(((w & 0x3FFFFFF) << 2 | 0x80000000, False))
            elif addr not in incode and 0x800C0000 <= w < 0x801E48B8 and w % 4 == 0:
                cands.append((w, True))
            # lui rX,hi / addiu|ori rX,rX,lo building a code pointer (callbacks)
            if addr in incode and w >> 26 == 0x0F:
                reg, hi = (w >> 16) & 31, w & 0xFFFF
                for k in range(1, 5):
                    if i + k >= len(words[n]): break
                    w2 = words[n][i + k]; op2 = w2 >> 26
                    if op2 in (0x09, 0x0D) and (w2 >> 21) & 31 == reg and (w2 >> 16) & 31 == reg:
                        lo = w2 & 0xFFFF
                        t = (hi << 16) + (lo - 0x10000 if op2 == 0x09 and lo & 0x8000 else lo)
                        cands.append((t & 0xFFFFFFFF, True))
                        break
            for t, need_prologue in cands:
                dests = [n] + (["main"] if n != "main" else ["jm2shell"])
                if n == "main":
                    dests = list(mods)       # main can call into whatever is loaded
                for d in dests:
                    tw = word_at(d, t)
                    if tw is None: continue
                    if need_prologue and not is_prologue(tw): continue
                    targets[d].add(t)

    total = 0
    for n in mods:
        fl = maps[n]["functions"]
        starts = {int(f["address"], 16): f for f in fl}
        added = 0
        for t in sorted(targets[n]):
            if t in starts: continue
            owner = next((f for f in fl if int(f["address"], 16) < t < int(f["address"], 16) + f["size"]), None)
            if owner is not None:
                a = int(owner["address"], 16); end = a + owner["size"]
                # only split when the entry is a real frame setup; a JAL into the
                # middle of a function body is a decoding accident, not a call
                if not is_prologue(word_at(n, t)): continue
                owner["size"] = t - a
            else:
                nxt = min((s for s in starts if s > t), default=mods[n][0] + len(mods[n][1]))
                end = t
                while end < nxt:
                    if word_at(n, end) == 0x03E00008:
                        end += 8; break
                    end += 4
                if not is_prologue(word_at(n, t)) and end - t < 8: continue
            f = {"address": "0x%08X" % t, "name": "func_%08X" % t, "size": end - t}
            fl.append(f); starts[t] = f; added += 1
        if added:
            fl.sort(key=lambda f: int(f["address"], 16))
            json.dump(maps[n], open(os.path.join(cfg, "funcmaps", n + ".json"), "w"), indent=2)
            print("[fix] %s: +%d function start(s)" % (n, added))
        total += added
    print("[fix] %d added in total" % total)

main()
