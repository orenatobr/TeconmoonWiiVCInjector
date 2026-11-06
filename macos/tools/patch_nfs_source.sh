#!/bin/sh
# Make the Windows-only nfs2iso2nfs source (piratesephiroth/nfs2iso2nfs_ps) work on macOS. Usage: patch_nfs_source.sh Program.cs
# 1. Paths were built with "\\": on macOS that creates files literally named "<cwd>\hif_000000.nfs" outside content/,
#    and looks for "..\code\htk.bin" as one file name. Use "/" in path strings (lines with Console output are left alone).
# 2. memcmp was imported from msvcrt.dll, which does not exist here; compare spans instead.
set -e
f="$1"
sed -E -i '' '/Console\.Write/! s/\\\\/\//g' "$f"
sed -E -i '' 's/b1\.Length == b2\.Length && memcmp\(b1, b2, b1\.Length\) == 0/b1.AsSpan().SequenceEqual(b2)/' "$f"
grep -q 'SequenceEqual(b2)' "$f" || { echo "patch_nfs_source: memcmp replacement did not apply" >&2; exit 1; }
