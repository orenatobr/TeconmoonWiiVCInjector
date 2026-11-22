#!/usr/bin/env bash
# Rebuilds the Nintendont audio experiment (or the untouched upstream, for comparison) with the toolchain
# that Nintendont's README pins: devkitARM r53-1, devkitPPC r35, libogc 1.8.23-1.
#
# Usage:  macos/nintendont-audio-exp/build.sh [half|upstream] [output.dol]
#   half      upstream 7a18345 plus half-stream-buffers.patch (default)
#   upstream  upstream 7a18345 as is
# Needs Docker (the toolchain is linux/amd64, so it runs under emulation on Apple silicon: expect 15 to 40 minutes),
# git, curl and shasum. The old toolchain packages come from an unofficial mirror; every file is checked against
# the sha256 below before it is used.
set -euo pipefail

VARIANT="${1:-half}"
OUT="${2:-$PWD/loader-$VARIANT.dol}"
HERE="$(cd "$(dirname "$0")" && pwd)"
NINTENDONT_REF=7a183459939653cb7bcf59ab137c2758a9f9fae0
GENERAL_TOOLS_REF=46086605cdc63fb02ba0ed08cdc00801ba00c6f0
GAMECUBE_TOOLS_REF=3b7f914df771d4b779d88607ceed2ec9264ff93a
MIRROR=https://wii.leseratte10.de/devkitPro

[[ "$VARIANT" == half || "$VARIANT" == upstream ]] || { echo "variant must be half or upstream" >&2; exit 2; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/tc"

fetch() { # <url> <sha256>
  local f="$WORK/tc/$(basename "$1")"
  curl -fsSL --retry 3 -o "$f" "$1"
  echo "$2  $f" | shasum -a 256 -c - >/dev/null || { echo "sha256 mismatch for $1" >&2; exit 1; }
}
fetch "$MIRROR/devkitARM/r53%20%282019-06%29/devkitARM-r53-1-linux.pkg.tar.xz" 140d217a747c21a46236a6675b2592b54397e53c56dd8aa91aaf73bd19fa9599
fetch "$MIRROR/devkitPPC/r35/devkitPPC-r35-1-linux.pkg.tar.xz" 2916d431913b00d93dd0570d2d650cb1ec13e75422933e0f5726528d6a181658
fetch "$MIRROR/libogc/libogc_1.8.23%20%282019-10-02%29/libogc-1.8.23-1-any.pkg.tar.xz" e5b61a2e7589ab95e321cae30bbf1511fbe4729f3120e782201eebf8514c6707
fetch "$MIRROR/devkitPPC/devkitppc-rules/devkitppc-rules-1.1.1-1-any.pkg.tar.xz" 0118f06fff938c3d4913fdc004d5edd2f72e3a16c544fb5699b0e97552529d29

git clone -q https://github.com/FIX94/Nintendont "$WORK/src"
git -C "$WORK/src" checkout -q "$NINTENDONT_REF"
[[ "$VARIANT" == half ]] && git -C "$WORK/src" apply "$HERE/half-stream-buffers.patch"
# Loader and kernel print their build time (__DATE__ __TIME__), so every build differs. The published boot.dol says
# "Nov 21 2026 14:25:37" (the loader's value); BUILD_DATE and BUILD_TIME override it. Every use in the tree is pinned.
BUILD_DATE="${BUILD_DATE:-Nov 21 2026}"; BUILD_TIME="${BUILD_TIME:-14:25:37}"
while IFS= read -r f; do
  sed -e "s/__DATE__/\"$BUILD_DATE\"/g" -e "s/__TIME__/\"$BUILD_TIME\"/g" "$f" > "$WORK/pinned.tmp" && mv "$WORK/pinned.tmp" "$f"
done < <(grep -rlE '__DATE__|__TIME__' "$WORK/src/loader/source" "$WORK/src/kernel" "$WORK/src/common" --include='*.c' --include='*.h')
if grep -rqE '__DATE__|__TIME__' "$WORK/src/loader/source" "$WORK/src/kernel" "$WORK/src/common"; then
  echo "could not pin __DATE__/__TIME__ everywhere" >&2; exit 1
fi
grep -q "$BUILD_DATE" "$WORK/src/loader/source/menu.c" || { echo "build time was not pinned in menu.c" >&2; exit 1; }
mkdir -p "$WORK/tools"
git clone -q https://github.com/devkitPro/general-tools "$WORK/tools/general-tools"
git -C "$WORK/tools/general-tools" checkout -q "$GENERAL_TOOLS_REF"
git clone -q https://github.com/devkitPro/gamecube-tools "$WORK/tools/gamecube-tools"
git -C "$WORK/tools/gamecube-tools" checkout -q "$GAMECUBE_TOOLS_REF"

cat > "$WORK/in-container.sh" <<'EOF'
set -e
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq && apt-get install -y -qq build-essential python3 zip xz-utils libncurses6 >/dev/null 2>&1
for f in /tc/*.pkg.tar.xz; do tar -xJf "$f" -C / 2>/dev/null; done
# bin2s and elf2dol come from devkitPro's general-tools and gamecube-tools (plain C), compiled here.
mkdir -p /opt/devkitpro/tools/bin
for t in bin2s padbin raw2c; do
  gcc -O2 -DPACKAGE_NAME=\"general-tools\" -DPACKAGE_VERSION=\"1.4\" -DPACKAGE_STRING=\"general-tools\ 1.4\" \
      -DPACKAGE_BUGREPORT=\"\" -o /opt/devkitpro/tools/bin/$t /gt/general-tools/$t.c
done
gcc -O2 -DPACKAGE_NAME=\"gamecube-tools\" -DPACKAGE_VERSION=\"1.0\" -DPACKAGE_STRING=\"gamecube-tools\ 1.0\" \
    -DPACKAGE_BUGREPORT=\"\" -o /opt/devkitpro/tools/bin/elf2dol /gt/gamecube-tools/elftool/elf2dol.c
export DEVKITPRO=/opt/devkitpro DEVKITARM=/opt/devkitpro/devkitARM DEVKITPPC=/opt/devkitpro/devkitPPC
export PATH=$DEVKITPPC/bin:$DEVKITARM/bin:/opt/devkitpro/tools/bin:$PATH
cd /src
make forced > /src/build.log 2>&1 || { tail -30 /src/build.log; exit 1; }
sha256sum loader/loader.dol
EOF

docker run --rm --platform linux/amd64 \
  -v "$WORK/tc":/tc:ro -v "$WORK/tools":/gt:ro -v "$WORK/src":/src -v "$WORK/in-container.sh":/in-container.sh:ro \
  ubuntu:22.04 bash /in-container.sh
cp "$WORK/src/loader/loader.dol" "$OUT"
echo "wrote $OUT"
