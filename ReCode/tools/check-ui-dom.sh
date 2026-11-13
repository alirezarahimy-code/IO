#!/usr/bin/env bash
# Offline browser-DOM contracts only: no SQL, worker, network device or SDK.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
checks=("$root"/tests/IO.Infrastructure.SqlTests/*-ui-check.cjs)
[[ -f "${checks[0]}" ]] || { echo 'No DOM checks found.' >&2; exit 1; }
for check in "${checks[@]}"; do
  node "$check"
done
printf 'PASS %s offline DOM test files; SQL and worker were NOT run.\n' "${#checks[@]}"
