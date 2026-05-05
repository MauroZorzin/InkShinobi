#!/usr/bin/env bash
set -euo pipefail

project_root="${1:-.}"
report_dir="${2:-artifacts/format}"
mkdir -p "$report_dir"
report_file="$report_dir/dotnet-format-report.json"

if ! find "$project_root/Assets" -type f -name '*.cs' -print -quit | grep -q .; then
  echo "No C# files found under Assets/. Skipping format check."
  printf '{"status":"skipped","reason":"No C# files found under Assets/."}
' > "$report_file"
  exit 0
fi

format_target="${DOTNET_FORMAT_TARGET:-}"
if [ -n "$format_target" ]; then
  if [ ! -f "$project_root/$format_target" ] && [ ! -f "$format_target" ]; then
    echo "DOTNET_FORMAT_TARGET was set to '$format_target', but that file was not found." >&2
    exit 1
  fi
else
  mapfile -t slnx_files < <(find "$project_root" -maxdepth 2 -type f -name '*.slnx' | sort)
  if [ "${#slnx_files[@]}" -eq 1 ]; then
    format_target="${slnx_files[0]}"
  elif [ "${#slnx_files[@]}" -gt 1 ]; then
    echo "Multiple .slnx files were found. Set DOTNET_FORMAT_TARGET explicitly." >&2
    printf '%s
' "${slnx_files[@]}" >&2
    exit 1
  else
    mapfile -t sln_files < <(find "$project_root" -maxdepth 2 -type f -name '*.sln' | sort)
    if [ "${#sln_files[@]}" -eq 1 ]; then
      format_target="${sln_files[0]}"
    elif [ "${#sln_files[@]}" -gt 1 ]; then
      echo "Multiple solution files were found. Set DOTNET_FORMAT_TARGET explicitly." >&2
      printf '%s
' "${sln_files[@]}" >&2
      exit 1
    fi
  fi
fi

if [ -n "$format_target" ]; then
  echo "Running dotnet format against solution: $format_target"
  dotnet format whitespace "$format_target"     --verify-no-changes     --report "$report_file"     --verbosity normal
else
  echo "No .slnx/.sln file found. Falling back to folder mode."
  dotnet format whitespace "$project_root"     --folder     --verify-no-changes     --include Assets     --exclude Library Logs Obj Temp Build Builds UserSettings Packages     --report "$report_file"     --verbosity normal
fi
