#!/usr/bin/env bash
# Real .NET domain + mocked DOM + evidence consistency checks only.
# This is NOT a substitute for full-solution build, SQL/HTTPS or Windows CI.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
if ! command -v dotnet >/dev/null 2>&1; then
  for candidate in "${DOTNET_ROOT:-}/dotnet" "$HOME/.dotnet/dotnet" "$HOME/.local/share/dotnet/dotnet"; do
    if [[ -x "$candidate" ]]; then
      export DOTNET_ROOT="$(dirname "$candidate")"
      export PATH="$DOTNET_ROOT:$PATH"
      break
    fi
  done
fi
if ! command -v dotnet >/dev/null 2>&1 || [[ ! "$(dotnet --version)" =~ ^10\. ]]; then
  echo 'BLOCKED: .NET 10 SDK is required; checked PATH, DOTNET_ROOT and user installation paths.' >&2
  exit 2
fi
for command in node python3; do
  command -v "$command" >/dev/null || { echo "BLOCKED: $command is required." >&2; exit 2; }
done
if ! node -e 'require("jsdom")' >/dev/null 2>&1; then
  echo 'BLOCKED: install jsdom@26.1.0 in an isolated npm prefix and set NODE_PATH to its node_modules.' >&2
  exit 2
fi
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
printf 'SDK: %s\n' "$(dotnet --version)"
feed="$(mktemp -d "${TMPDIR:-/tmp}/io-part2-empty-feed.XXXXXX")"
trap 'rmdir "$feed"' EXIT
# Domain has no external packages. An empty source makes this check network-independent.
dotnet restore tests/IO.Domain.Tests/IO.Domain.Tests.csproj --source "$feed"
dotnet run --project tests/IO.Domain.Tests --configuration Release --no-restore
for file in src/IO.Web/Ui/*.js tests/IO.Infrastructure.SqlTests/*.cjs; do node --check "$file"; done
node tests/IO.Infrastructure.SqlTests/reversal-ui-check.cjs
python3 - <<'PY'
from pathlib import Path
import csv, json
p=Path('docs/part-02')
def rows(name):
    with (p/name).open(encoding='utf-8-sig',newline='') as f:
        return list(csv.DictReader(f))
a,b=rows('device-view-evidence.csv'),rows('device-operation-coverage.csv')
key=lambda r:(r['file'],r['line'],r['kind'],r['identifier'])
assert a and [key(r) for r in a]==[key(r) for r in b], 'UI coverage differs from extracted evidence'
assert len(a)==json.loads((p/'device-audit-summary.json').read_text())['view_evidence']
assert all(r['status']=='must-build' and r['target'].startswith('S04:') for r in b if '/Gate/' in r['file'])
exports,coverage=rows('device-sdk-exports.csv'),rows('device-sdk-coverage.csv')
assert [(r['file'],r['export']) for r in exports]==[(r['file'],r['export']) for r in coverage]
assert any(r['export']=='Z_Unlock' and r['status']=='outside-scope' for r in coverage)
assert all(r['status']=='conditional' for r in coverage if r['export'] in ('ReadBlock_LargeTmp','Z_EnableClock'))
print(f'PASS evidence consistency: {len(a)} UI rows, {len(exports)} exports; grouping preserved and exact physical commands excluded')
PY
printf '%s\n' 'PASS local offline subset only; full build, SQL/HTTPS/browser integration and Windows acceptance remain separate.'
