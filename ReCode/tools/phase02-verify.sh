#!/usr/bin/env bash
# Verify the actual .NET foundation. No database or legacy program is touched.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'BLOCKED: .NET 10 SDK not found on PATH. No build or C# checks were run.' >&2
  exit 2
fi
if ! version=$(dotnet --version); then
  echo 'BLOCKED: dotnet could not select an SDK matching global.json.' >&2
  exit 2
fi
if [[ ! "$version" =~ ^10\. ]]; then
  echo "BLOCKED: expected .NET 10 SDK; selected $version." >&2
  exit 2
fi
printf 'Selected SDK: %s\n' "$version"
dotnet --info
dotnet build IO.slnx --configuration Release
dotnet run --project tests/IO.Domain.Tests/IO.Domain.Tests.csproj --configuration Release --no-build
printf '%s\n' 'PASS: build and executable domain checks completed. SQL and browser tests are not included.'
