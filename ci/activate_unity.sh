#!/usr/bin/env bash
set -euo pipefail

artifacts_dir="${1:-artifacts}"
mkdir -p "$artifacts_dir"

if [ -z "${UNITY_SERIAL:-}" ] || [ -z "${UNITY_EMAIL:-}" ] || [ -z "${UNITY_PASSWORD:-}" ]; then
  echo "Missing UNITY_SERIAL, UNITY_EMAIL, or UNITY_PASSWORD GitLab CI/CD variables." >&2
  exit 1
fi

xvfb-run --auto-servernum --server-args='-screen 0 640x480x24' \
  unity-editor \
  -batchmode \
  -nographics \
  -quit \
  -username "$UNITY_EMAIL" \
  -password "$UNITY_PASSWORD" \
  -serial "$UNITY_SERIAL" \
  -logFile "$artifacts_dir/activation.log"

cat "$artifacts_dir/activation.log"
