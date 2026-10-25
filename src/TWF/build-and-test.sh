#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"
FACADES_DIR="/tmp/netfx-facades"
mkdir -p "$BUILD_DIR" "$FACADES_DIR"

FX_DIR="/tmp/dotnet/package/shared/Microsoft.NETCore.App/2.0.0"
SQL_DLL="/tmp/dotnet/package/store/x64/netcoreapp2.0/system.data.sqlclient/4.4.0/runtimes/unix/lib/netstandard2.0/System.Data.SqlClient.dll"
# Engine assemblies come from the PINNED build (engine/engine-pin.json),
# never from the legacy TWF 4.28 folder. tools/verify-engine-pin.py --check
# must pass before a build is considered valid.
PIN_FILE="$SCRIPT_DIR/engine/engine-pin.json"
PIN_SOURCE="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["pinned_from"])' "$PIN_FILE")"
BIN_DIR="$REPO_ROOT/$PIN_SOURCE"
CSC="/usr/local/bin/csc"
DOTNET="/usr/local/bin/dotnet"

REF_ARGS=("-r:$SQL_DLL")
for dll in \
  mscorlib \
  netstandard \
  System \
  System.Core \
  System.Data \
  System.Data.Common \
  System.Xml \
  System.Runtime \
  System.Runtime.Extensions \
  System.Collections \
  System.Collections.Concurrent \
  System.Collections.NonGeneric \
  System.Collections.Specialized \
  System.Console \
  System.Globalization \
  System.Globalization.Calendars \
  System.IO \
  System.IO.FileSystem \
  System.Linq \
  System.Linq.Expressions \
  System.Private.CoreLib \
  System.Private.Uri \
  System.Security.Principal \
  System.Threading \
  System.Threading.Thread \
  System.Threading.Tasks \
  System.Text.Encoding \
  System.Text.RegularExpressions \
  System.Diagnostics.Debug \
  System.ObjectModel \
  System.ComponentModel.Primitives \
  System.ComponentModel.TypeConverter \
  System.Security.Cryptography.Algorithms \
  System.Security.Cryptography.Csp \
  System.Security.Cryptography.Primitives \
  System.Xml.ReaderWriter \
  System.Xml.XDocument \
  System.Private.Xml \
  System.Private.Xml.Linq \
  Microsoft.CSharp \
  System.Dynamic.Runtime; do
  if [ -f "$FX_DIR/$dll.dll" ]; then
    REF_ARGS+=("-r:$FX_DIR/$dll.dll")
  fi
done

echo "[1/7] Compiling .NET Framework 4.0 Reference Facades..."
"$CSC" -nologo -target:library -out:"$FACADES_DIR/System.Configuration.dll" \
  "${REF_ARGS[@]}" \
  -keyfile:"$SCRIPT_DIR/BuildTools/msft-pub.snk" -delaysign+ \
  "$SCRIPT_DIR/BuildTools/NetFxAssemblyVersion.cs" \
  "$SCRIPT_DIR/BuildTools/System.Configuration.Ref.cs"

"$CSC" -nologo -target:library -out:"$FACADES_DIR/System.Drawing.dll" \
  "${REF_ARGS[@]}" \
  -keyfile:"$SCRIPT_DIR/BuildTools/msft-pub.snk" -delaysign+ \
  "$SCRIPT_DIR/BuildTools/NetFxAssemblyVersion.cs" \
  "$SCRIPT_DIR/BuildTools/System.Drawing.Ref.cs"

"$CSC" -nologo -target:library -out:"$FACADES_DIR/System.Web.dll" \
  "${REF_ARGS[@]}" \
  -r:"$FACADES_DIR/System.Configuration.dll" \
  -keyfile:"$SCRIPT_DIR/BuildTools/msft-pub.snk" -delaysign+ \
  "$SCRIPT_DIR/BuildTools/NetFxAssemblyVersion.cs" \
  "$SCRIPT_DIR/BuildTools/System.Web.Ref.cs"

for dll in System.Configuration System.Drawing System.Web; do
  REF_ARGS+=("-r:$FACADES_DIR/$dll.dll")
  cp -f "$FACADES_DIR/$dll.dll" "$BUILD_DIR/"
done

for dll in \
  System.Web.Mvc \
  System.Web.Routing \
  System.Web.Abstractions \
  System.Web.Http \
  System.Web.Http.WebHost \
  System.Web.Optimization \
  System.Web.Razor \
  System.Web.WebPages \
  System.Web.WebPages.Deployment \
  System.Web.WebPages.Razor \
  WebGrease \
  CaptchaMvc \
  Dapper \
  T5V3.Model \
  T5V3.DB \
  T5V3.Misc \
  T5V3.Attendance \
  T5V3.LeaveCalcs \
  T5V3.Report \
  T5V3.ReportGenerator \
  Personnel \
  Stimulsoft.Report \
  Stimulsoft.Report.Web; do
  if [ -f "$BIN_DIR/$dll.dll" ]; then
    REF_ARGS+=("-r:$BIN_DIR/$dll.dll")
    cp -f "$BIN_DIR/$dll.dll" "$BUILD_DIR/"
  fi
done

# Normalize 32BITREQUIRED CLR header flag on pure-IL legacy assemblies so 64-bit Linux CoreCLR loads them cleanly
python3 - "$BUILD_DIR" << 'PYEOF'
import glob, os, struct, sys
build_dir = sys.argv[1]
for path in glob.glob(os.path.join(build_dir, "*.dll")):
    with open(path, "rb") as f:
        data = bytearray(f.read())
    if data[:2] != b"MZ":
        continue
    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_offset:pe_offset+4] != b"PE\x00\x00":
        continue
    opt_magic = struct.unpack_from("<H", data, pe_offset + 24)[0]
    dd_offset = pe_offset + 24 + (96 if opt_magic == 0x10B else 112) + 14 * 8
    clr_rva, clr_size = struct.unpack_from("<II", data, dd_offset)
    if clr_rva == 0:
        continue
    num_sections = struct.unpack_from("<H", data, pe_offset + 6)[0]
    opt_size = struct.unpack_from("<H", data, pe_offset + 20)[0]
    sec_offset = pe_offset + 24 + opt_size
    for i in range(num_sections):
        s_off = sec_offset + i * 40
        v_size, v_addr, r_size, r_ptr = struct.unpack_from("<IIII", data, s_off + 8)
        if v_addr <= clr_rva < v_addr + max(v_size, r_size):
            clr_file_offset = r_ptr + (clr_rva - v_addr)
            flags = struct.unpack_from("<I", data, clr_file_offset + 16)[0]
            if flags & 0x02:
                struct.pack_into("<I", data, clr_file_offset + 16, flags & ~0x02)
                with open(path, "wb") as f:
                    f.write(data)
            break
PYEOF

echo "[2/7] Compiling TD5.dll (Complete Rewritten Dapper Data Access Layer)..."
"$CSC" -nologo -target:library -out:"$BUILD_DIR/TD5.dll" \
  "${REF_ARGS[@]}" \
  $(find "$SCRIPT_DIR/TD5" -name "*.cs" | sort)

echo "[3/7] Compiling TWF.Domain.dll..."
"$CSC" -nologo -target:library -out:"$BUILD_DIR/TWF.Domain.dll" \
  "${REF_ARGS[@]}" \
  $(find "$SCRIPT_DIR/TWF.Domain" -name "*.cs" | sort)

echo "[4/7] Compiling TWF.Infrastructure.dll..."
"$CSC" -nologo -target:library -out:"$BUILD_DIR/TWF.Infrastructure.dll" \
  "${REF_ARGS[@]}" \
  -r:"$BUILD_DIR/TD5.dll" \
  -r:"$BUILD_DIR/TWF.Domain.dll" \
  $(find "$SCRIPT_DIR/TWF.Infrastructure" -name "*.cs" | sort)

echo "[5/7] Compiling TWF.Application.dll..."
"$CSC" -nologo -target:library -out:"$BUILD_DIR/TWF.Application.dll" \
  "${REF_ARGS[@]}" \
  -r:"$BUILD_DIR/TD5.dll" \
  -r:"$BUILD_DIR/TWF.Domain.dll" \
  -r:"$BUILD_DIR/TWF.Infrastructure.dll" \
  $(find "$SCRIPT_DIR/TWF.Application" -name "*.cs" | sort)

echo "[6/7] Compiling TWF.Web.dll (MyTime3 Core, Zero-Lock, Auth, 33 Models & All 34 Controllers)..."
"$CSC" -nologo -target:library -out:"$BUILD_DIR/TWF.Web.dll" \
  "${REF_ARGS[@]}" \
  -r:"$BUILD_DIR/TD5.dll" \
  -r:"$BUILD_DIR/TWF.Domain.dll" \
  -r:"$BUILD_DIR/TWF.Infrastructure.dll" \
  -r:"$BUILD_DIR/TWF.Application.dll" \
  $(find "$SCRIPT_DIR/TWF.Web" -name "*.cs" | sort)

echo "[7/7] Compiling TWF.Tests.dll & Running Verification Suite..."
"$CSC" -nologo -target:exe -out:"$BUILD_DIR/TWF.Tests.dll" \
  "${REF_ARGS[@]}" \
  -r:"$BUILD_DIR/TD5.dll" \
  -r:"$BUILD_DIR/TWF.Domain.dll" \
  -r:"$BUILD_DIR/TWF.Infrastructure.dll" \
  -r:"$BUILD_DIR/TWF.Application.dll" \
  -r:"$BUILD_DIR/TWF.Web.dll" \
  $(find "$SCRIPT_DIR/TWF.Tests" -name "*.cs" | sort)

cp -f "$SQL_DLL" "$BUILD_DIR/"

cat > "$BUILD_DIR/TWF.Tests.runtimeconfig.json" << 'EOF'
{
  "runtimeOptions": {
    "tfm": "netcoreapp2.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "2.0.0"
    }
  }
}
EOF

echo ""
"$DOTNET" "$BUILD_DIR/TWF.Tests.dll"
