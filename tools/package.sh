#!/bin/sh
# Build the release kit: sources + RecompOne snapshot + JetMoto2.exe (launcher only, no game code).
#   sh tools/package.sh 0.1.0
set -e
cd "$(dirname "$0")/.."
VER="$1"; [ -n "$VER" ] || { echo "usage: package.sh <version>"; exit 1; }
OUT="dist/JetMoto2-Recomp-v$VER-windows-x64"
rm -rf dist && mkdir -p "$OUT"
dotnet publish tools/Launcher/JetMoto2.csproj -c Release -o dist-launcher --nologo -v q
git ls-files --recurse-submodules | grep -v -E '^(\.gitmodules|docs/)' | while read -r f; do
    mkdir -p "$OUT/$(dirname "$f")" && cp "$f" "$OUT/$f"
done
cp dist-launcher/JetMoto2.exe "$OUT/"
printf '%s' "v$VER" > "$OUT/version.txt"
# refuse to ship anything derived from a disc
if find "$OUT" \( -iname '*.bin' -o -iname '*.cue' -o -path '*/generated/*' -o -path '*/build/*' \) | grep -q .; then
    echo "disc-derived files in the kit"; exit 1
fi
(cd dist && powershell -NoProfile -Command "Compress-Archive -Path '$(basename "$OUT")\*' -DestinationPath '$(basename "$OUT").zip'")
ls -la dist/*.zip
