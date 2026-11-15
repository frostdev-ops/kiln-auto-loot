#!/bin/sh
# SPDX-License-Identifier: MIT
# Assembles impact/smash_one.kimp (written from scratch, no vanilla code) into assets/impact/smash_one.bin with
# Kiln's tools/impact (docs/kiln/modding/impact.md), which lints it with the verifier kiln.abilities applies.
# The blob is shipped (not under assets/cooked: it holds no game data); assets/assets.json names it.
#   tools/smash/build_program.sh [KILN_TREE]      (the Kiln 0.2.0-dev.28 tree; default $KILN_TREE)
set -eu
K=${1:-${KILN_TREE:?pass the Kiln 0.2.0-dev.28 tree as the first argument or set KILN_TREE}}
HERE=$(cd "$(dirname "$0")/../.." && pwd)
export KILN_OUT=${KILN_OUT:-/tmp/kiln-auto-loot-impact}
python3 -I "$K/tools/impact/impact.py" asm "$HERE/impact/smash_one.kimp" -o "$HERE/assets/impact/smash_one.bin"
python3 -I "$K/tools/impact/impact.py" lint "$HERE/assets/impact/smash_one.bin"
