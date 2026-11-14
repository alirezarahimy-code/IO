#!/usr/bin/env bash
# Offline browser-DOM contracts only: no SQL, worker, network device or SDK.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
checks=("$root"/tests/IO.Infrastructure.SqlTests/*-ui-check.cjs)
[[ -f "${checks[0]}" ]] || { echo 'No DOM checks found.' >&2; exit 1; }
for check in "${checks[@]}"; do
  log="$(mktemp)"
  if ! node "$check" > "$log" 2>&1; then
    cat "$log"
    python "$root/tools/phase03-annotate-error.py" "$log" "Windows DOM contract failure: $(basename "$check")"
    rm -f "$log"
    exit 1
  fi
  cat "$log"
  rm -f "$log"
done
printf 'PASS %s offline DOM test files; SQL and worker were NOT run.\n' "${#checks[@]}"
