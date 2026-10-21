#!/bin/sh
# Jet Moto 2 Recompiled - regenerate the C# from the disc, then build.
#   sh rebuild.sh          recompile + build
#   sh rebuild.sh --maps   also redo the function maps first (after a RecompOne update)
set -e
cd "$(dirname "$0")"
CUE="disc/Jet Moto 2 - Championship Edition (USA).cue"
dotnet build RecompOne/RecompOne.Recompiler -c Release -v q --nologo | grep -E " error " || true
if [ "$1" = "--maps" ] || [ ! -f config/funcmaps/main.json ]; then
    dotnet run --project tools/FuncMapGen -c Release -- "$CUE" config \
        RecompOne/RecompOne.Recompiler/AutoConfigure/signatures/psyq.json | grep "^\[funcmap\]"
    python -I tools/fix_funcmaps.py "$CUE" config
fi
python -I tools/merge_config.py config
RecompOne/RecompOne.Recompiler/bin/Release/net10.0/recompone.exe config/JetMoto2.json > recompile.log 2>&1 || { tail -20 recompile.log; exit 1; }
grep -E "WARNING|applied" recompile.log || true
python -I tools/apply_fixups.py .
dotnet build JetMoto2Recomp.csproj -c Release -o build --nologo 2>&1 | grep -E " error |Build succeeded"
