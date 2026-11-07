#!/usr/bin/env bash
# Build the Windows player with the Unity CLI and run the autopilot test drive.
# Usage: ./autotest.sh <outdir>
set -e
cd "$(dirname "$0")"
OUT="${1:-Logs/autopilot}"
unity build . --target StandaloneWindows64 --execute-method ShiftDriveSetup.BuildWindows -o Builds/Windows --no-tail -l Logs/build-win.log > /dev/null 2>&1
grep -E "error CS|\[ShiftDrive\] Build" Logs/build-win.log | sort -u
rm -rf "$OUT"
./Builds/Windows/ShiftDrive.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -autopilot "$OUT" -logFile "$OUT-player.log"
cat "$OUT/autopilot.log"
