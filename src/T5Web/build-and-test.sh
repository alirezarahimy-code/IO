#!/usr/bin/env bash
# ============================================================================
# T5Web Admin Console — build and verification
# ----------------------------------------------------------------------------
# The deployment network is offline, so this script never downloads anything.
# It runs the same static gates as the ARIA build (engine pin, schema manifest,
# SQL conformance, C# lint) plus the engine model contract, and only then compiles.
#
#   ./build-and-test.sh            static gates + compile + run tests
#   ./build-and-test.sh --gates    static gates only (works without a compiler)
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ARIA_TOOLS="$(cd "$SCRIPT_DIR/../TWF/tools" && pwd)"
PIN_FILE="$SCRIPT_DIR/../TWF/engine/engine-pin.json"
PIN_SOURCE="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["pinned_from"])' "$PIN_FILE")"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BIN_DIR="$REPO_ROOT/$PIN_SOURCE"
BUILD_DIR="$SCRIPT_DIR/build"
GATES_ONLY="false"
[ "${1:-}" = "--gates" ] && GATES_ONLY="true"

echo "[1/4] Static gates"
python3 "$ARIA_TOOLS/verify-engine-pin.py" --check
python3 "$ARIA_TOOLS/build-schema-manifest.py" --check
python3 "$ARIA_TOOLS/verify-sql-schema.py"
python3 "$ARIA_TOOLS/lint-csharp.py"
# The portal reads organisational data through the engine's DA layer, so the engine
# model members it names must exist in the pinned build (gate T14, BUG-01..BUG-07).
python3 "$SCRIPT_DIR/tools/engine-model-contract.py" --check
# Column types the generic module screens bind to (T16). DDL wins, engine ORM fills gaps.
python3 "$SCRIPT_DIR/tools/engine-column-types.py" --check
# Coverage: every public action of the legacy web assembly has a recorded decision and
# every target really exists in this solution (T25 controller-level, T27 action-level).
python3 "$SCRIPT_DIR/tools/legacy-coverage.py" --check
# Data model: every table has a catalog, every key has a proof, every reference a target,
# and no SQL statement joins two catalogs (T28).
python3 "$SCRIPT_DIR/tools/engine-relations.py" --check
# Syntax: braces balancing is not syntax. A real C# grammar parses every file (T35).
# Type checking is impossible here (no compiler, no .NET Framework) and is done by the
# Windows CI job instead - see docs/14.
python3 "$SCRIPT_DIR/tools/csharp-parse-gate.py"
# Usings: a type that is used needs its namespace imported. Three CI rounds went to
# CS0103/CS0246 errors that this finds in a second.
python3 "$SCRIPT_DIR/tools/using-gate.py"
# Build inputs: every .csproj parses as XML and declares Debug/Release|AnyCPU. The
# Windows CI found four projects with no configuration at all, and then a project file
# that a text substitution had turned into broken XML (BUG-32).
python3 "$SCRIPT_DIR/tools/project-file-gate.py"
# Column coverage: every `alias.[Column]` in the data layer must exist in the typed
# model (or in the portal's own DDL). The Windows CI found this class four times in a
# row; now a build finds it, not a CI round trip.
python3 "$SCRIPT_DIR/tools/sql-column-coverage.py"
# SQL executable against the model: the synthetic schema stands in for the vendor
# install, and the smoke tool must still extract the statements it compiles (T35).
python3 "$SCRIPT_DIR/tools/synthetic-schema.py" --check
python3 "$SCRIPT_DIR/tools/sql-smoke.py" --check
# Grants: the least-privilege login must be allowed to write exactly the tables the
# generic modules write, in the catalog that owns them (BUG found by CI on Windows).
python3 "$SCRIPT_DIR/tools/sql-grant-gate.py"
python3 "$SCRIPT_DIR/tools/sql-grant-gate.py" --self-test
# Deployment kit: the Windows install scripts must parse, stay idempotent, keep passwords
# out of the repository and reference files that exist (docs/13).
python3 "$SCRIPT_DIR/tools/deploy-kit-gate.py"
# Module fields: every column a generic screen names must exist in the engine model. A
# field that does not is "Invalid column name" on one screen at the first click; the
# statement is assembled from the descriptor, so nothing else can see it (BUG-40).
python3 "$SCRIPT_DIR/tools/module-field-gate.py"
# Fixture: tools/ci-seed.sql must cover every AdminFeature and every NOT NULL column of
# [UserInfo], or a screen answers 403 on the runner and the sweep passes without it.
python3 "$SCRIPT_DIR/tools/ci-fixture-gate.py"
# Migration gate: the fingerprint script must cover every t5/personnel table exactly once,
# guard a table that an older install lacks, and its comparison logic must be tested (T33).
python3 "$SCRIPT_DIR/tools/migration-fingerprint.py" --check

if [ "$GATES_ONLY" = "true" ]; then
  echo "OK: static gates passed (compiler step skipped)"
  exit 0
fi

CSC="${CSC:-csc}"
if ! command -v "$CSC" >/dev/null 2>&1; then
  cat <<'MSG'
ERROR: no C# compiler found.

On Windows (the target platform) build with MSBuild instead:
    msbuild T5Web.sln /p:Configuration=Release
    T5Web.Tests\bin\Release\T5Web.Tests.exe

Or point CSC at a Roslyn/Mono compiler and re-run:
    CSC=/path/to/csc ./build-and-test.sh
MSG
  exit 2
fi

mkdir -p "$BUILD_DIR"
echo "[2/4] Compiling libraries"
COMMON_REF=()
for dll in System System.Core System.Data System.Configuration System.Web; do
  COMMON_REF+=("-r:$dll")
done
COMMON_REF+=("-r:$BIN_DIR/System.Web.Mvc.dll" "-r:$BIN_DIR/Dapper.dll")
ENGINE_REF=("-r:$BIN_DIR/Personnel.dll" "-r:$BIN_DIR/T5V3.DB.dll" "-r:$BIN_DIR/T5V3.Model.dll")

"$CSC" -nologo -target:library -langversion:7.3 -out:"$BUILD_DIR/T5Web.Domain.dll" \
  "${COMMON_REF[@]}" $(find "$SCRIPT_DIR/T5Web.Domain" -name '*.cs' | sort)

"$CSC" -nologo -target:library -langversion:7.3 -out:"$BUILD_DIR/T5Web.Application.dll" \
  "${COMMON_REF[@]}" -r:"$BUILD_DIR/T5Web.Domain.dll" \
  $(find "$SCRIPT_DIR/T5Web.Application" -name '*.cs' | sort)

"$CSC" -nologo -target:library -langversion:7.3 -out:"$BUILD_DIR/T5Web.Data.dll" \
  "${COMMON_REF[@]}" "${ENGINE_REF[@]}" -r:"$BUILD_DIR/T5Web.Domain.dll" \
  $(find "$SCRIPT_DIR/T5Web.Data" -name '*.cs' | sort)

echo "[3/4] Compiling the portal"
"$CSC" -nologo -target:library -langversion:7.3 -out:"$BUILD_DIR/T5Web.Admin.dll" \
  "${COMMON_REF[@]}" \
  -r:"$BUILD_DIR/T5Web.Domain.dll" \
  -r:"$BUILD_DIR/T5Web.Application.dll" \
  -r:"$BUILD_DIR/T5Web.Data.dll" "${ENGINE_REF[@]}" \
  $(find "$SCRIPT_DIR/T5Web.Web" -name '*.cs' | sort)

echo "[4/4] Compiling and running the verification suite"
"$CSC" -nologo -target:exe -langversion:7.3 -out:"$BUILD_DIR/T5Web.Tests.exe" \
  "${COMMON_REF[@]}" \
  -r:"$BUILD_DIR/T5Web.Domain.dll" \
  -r:"$BUILD_DIR/T5Web.Application.dll" \
  -r:"$BUILD_DIR/T5Web.Data.dll" "${ENGINE_REF[@]}" \
  $(find "$SCRIPT_DIR/T5Web.Tests" -name '*.cs' | sort)

mono "$BUILD_DIR/T5Web.Tests.exe" || "$BUILD_DIR/T5Web.Tests.exe"
