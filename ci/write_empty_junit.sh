#!/usr/bin/env bash
set -euo pipefail

suite="${1:?suite is required}"
xml_path="${2:?xml_path is required}"
mkdir -p "$(dirname "$xml_path")"

cat > "$xml_path" <<XML
<?xml version="1.0" encoding="utf-8"?>
<testsuite name="$suite" tests="0" failures="0" errors="0" skipped="0" time="0" />
XML
