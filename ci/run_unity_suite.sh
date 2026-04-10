#!/usr/bin/env bash
set -euo pipefail

suite="${1:?suite is required}"
project_path="${2:-.}"
results_dir="${3:-artifacts/test-results}"
mkdir -p "$results_dir"

xml_path="$results_dir/${suite}.xml"
log_path="$results_dir/${suite}.log"

if ! ./ci/has_unity_tests.sh "$suite" "$project_path"; then
  echo "No likely ${suite} test files found. Emitting an empty JUnit report."
  ./ci/write_empty_junit.sh "$suite" "$xml_path"
  printf 'No likely %s test files found.\n' "$suite" > "$log_path"
  exit 0
fi

set +e
xvfb-run --auto-servernum --server-args='-screen 0 640x480x24' \
  unity-editor \
  -batchmode \
  -nographics \
  -accept-apiupdate \
  -projectPath "$project_path" \
  -runTests \
  -testPlatform "$suite" \
  -testResults "$xml_path" \
  -logFile "$log_path"
status=$?
set -e

cat "$log_path"

if [ ! -f "$xml_path" ]; then
  if grep -qiE 'no tests|0 tests|test(s)? run: 0' "$log_path"; then
    echo "Unity reported no ${suite} tests. Emitting an empty JUnit report."
    ./ci/write_empty_junit.sh "$suite" "$xml_path"
    exit 0
  fi
  echo "Unity did not produce a ${suite} XML results file." >&2
  exit "${status:-1}"
fi

exit "$status"
