#!/bin/bash
# Compiles loader/source/GameSettings.c, as teconmoon.patch adds it, with the host compiler and runs gamesettings_test.c.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
cd "$T"
git init -q
git apply --include='loader/source/GameSettings.*' "$HERE/teconmoon.patch"
cc -Wall -Wextra -Iloader/source -o t "$HERE/gamesettings_test.c" loader/source/GameSettings.c
./t
