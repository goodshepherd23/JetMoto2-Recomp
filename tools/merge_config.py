#!/usr/bin/env python3
"""config/JetMoto2.base.json (from FuncMapGen) + config/patches.json -> config/JetMoto2.json"""
import json, os, sys
cfg = sys.argv[1]
c = json.load(open(os.path.join(cfg, "JetMoto2.base.json")))
c["patches"] = json.load(open(os.path.join(cfg, "patches.json")))
# The game uses libpad's PadInit/PadRead entry points, which RecompOne's libpad
# replacements do not cover; run the game's own libpad against the emulated SIO0.
c["disableHle"] = ["libpad"]
json.dump(c, open(os.path.join(cfg, "JetMoto2.json"), "w"), indent=2)
