#!/usr/bin/env bash
set -euo pipefail

project_root="${1:-.}"
report_path="${2:-artifacts/static-analysis/unity-pattern-report.txt}"
mkdir -p "$(dirname "$report_path")"
: > "$report_path"

critical_count=0
warning_count=0

runtime_files() {
  find "$project_root/Assets" -type f -name '*.cs' \
    ! -path '*/Editor/*' \
    ! -path '*/Tests/*' \
    ! -path '*/Test/*'
}

append_matches() {
  local label="$1"
  local file="$2"
  local pattern="$3"
  local severity="$4"
  local tmp
  tmp="$(mktemp)"
  if grep -nE "$pattern" "$file" > "$tmp"; then
    {
      printf '%s: %s\n' "$severity" "$label"
      printf 'File: %s\n' "$file"
      cat "$tmp"
      printf '\n'
    } >> "$report_path"
    local count
    count="$(wc -l < "$tmp" | tr -d ' ')"
    if [ "$severity" = "CRITICAL" ]; then
      critical_count=$((critical_count + count))
    else
      warning_count=$((warning_count + count))
    fi
  fi
  rm -f "$tmp"
}

while IFS= read -r file; do
  append_matches "Avoid LINQ in runtime code" "$file" '^[[:space:]]*using[[:space:]]+System\.Linq[[:space:]]*;' CRITICAL
  append_matches "Avoid scene-wide searches in runtime code" "$file" '\b(FindObjectOfType|FindObjectsOfType|GameObject\.Find|FindFirstObjectByType|FindAnyObjectByType)\b' CRITICAL
  append_matches "Avoid DestroyImmediate in runtime code" "$file" '\bDestroyImmediate[[:space:]]*\(' CRITICAL
  append_matches "Cache yield instructions instead of allocating them repeatedly" "$file" 'new[[:space:]]+WaitForSeconds(Realtime)?[[:space:]]*\(' WARNING

done < <(runtime_files)

while IFS= read -r file; do
  awk -v file="$file" '
    BEGIN {
      in_hot = 0;
      brace_depth = 0;
      method_depth = 0;
    }
    {
      line = $0;
      if (line ~ /^[[:space:]]*\/\//) {
        next;
      }

      if (!in_hot && line ~ /(^|[[:space:]])(private|protected|public|internal|static|virtual|override|sealed|new|async|partial|extern|unsafe|[[:alnum:]_<>,\[\]?]+[[:space:]]+)*(Update|LateUpdate|FixedUpdate)[[:space:]]*\(/) {
        in_hot = 1;
        method_depth = brace_depth;
      }

      if (in_hot && line ~ /\bGetComponent[[:space:]]*</) {
        printf("CRITICAL: Avoid GetComponent inside Update/LateUpdate/FixedUpdate\nFile: %s\n%d:%s\n\n", file, NR, line);
      }

      opens = gsub(/\{/, "{", line);
      closes = gsub(/\}/, "}", line);
      brace_depth += opens - closes;

      if (in_hot && brace_depth <= method_depth) {
        in_hot = 0;
      }
    }
  ' "$file" >> "$report_path"
done < <(runtime_files)

hot_matches="$(grep -c '^CRITICAL: Avoid GetComponent inside Update/LateUpdate/FixedUpdate$' "$report_path" || true)"
critical_count=$((critical_count + hot_matches))

{
  printf 'Unity static analysis summary\n'
  printf 'Critical findings: %s\n' "$critical_count"
  printf 'Warnings: %s\n\n' "$warning_count"
} | cat - "$report_path" > "$report_path.tmp"
mv "$report_path.tmp" "$report_path"

cat "$report_path"

if [ "$critical_count" -gt 0 ]; then
  echo "Static Unity pattern checks failed." >&2
  exit 1
fi
