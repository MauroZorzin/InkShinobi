#!/usr/bin/env bash
set -euo pipefail

suite="${1:?suite is required}"
project_root="${2:-.}"

has_match=1

case "$suite" in
  EditMode)
    if find "$project_root/Assets" -type f -name '*.cs' \
      \( -path '*/Editor/*' -o -path '*/EditMode/*' \) \
      -print -quit | grep -q .; then
      has_match=0
    fi
    ;;
  PlayMode)
    if find "$project_root/Assets" -type f -name '*.cs' \
      \( -path '*/PlayMode/*' -o -path '*/Tests/*' \) \
      ! -path '*/Editor/*' \
      -print -quit | grep -q .; then
      has_match=0
    fi
    ;;
  *)
    echo "Unknown suite: $suite" >&2
    exit 2
    ;;
esac

exit "$has_match"
