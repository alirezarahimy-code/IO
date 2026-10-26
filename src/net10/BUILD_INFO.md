# .NET 10 migration — baseline toolchain and immutable references

This records observations, not a successful .NET 10 release. The existing MVC 5 rewrites and the vendor programs are read-only references. New products belong exclusively under `src/net10/Aria/` and `src/net10/AriaAdmin/` on branch `arena/a4df3135-io`.

| Item | Observed value / proof |
| --- | --- |
| Read-only source checkpoint | `ebd8dff4c82ae762568426047829dcfac9089871` (current branch head at start) |
| TWF reference | `TWF 4.28/App/` and `src/TWF/` (current rewrite, `.NET Framework 4.7.2`, C# 7.3) |
| Aria Admin references | `T5Web [3.5.7]/App/`, `T5V3.9.2/` (desktop, supplementary), and `src/T5Web/` (current rewrite, `.NET Framework 4.7.2`, C# 7.3) |
| Link5 reference | `Link5 v4.0.4/` binary-only; untouched and outside the port |
| Last verified CI for existing rewrite | [run 40337319605](https://github.com/alirezarahimy-code/IO/actions/runs/40337319605), commit `ebd8dff`, 4 jobs succeeded: MSBuild/tests, IIS synthetic install, SQL synthetic scripts, portability spike |
| Windows CI OS/runtime | Windows Server 2025 `10.0.26100`, .NET Framework `4.8.1` (release 533509), MSBuild `18.11.1.51303` (CI check-run annotations) |
| IIS/SQL tested by that run | IIS 10.0, Framework `v4.0` pool with 32-bit enabled; SQL Server 2025 Express `17.0.4025.3` compatibility 170; **not** customer SQL or SQL Server 2016 |
| Portable T5Web lower layers | Existing `src/T5Web/spike/net8/Net8Spike.csproj` built for `net8.0` and `net10.0`, 0 reported compiler errors/warnings. Does **not** compile the web app or execute its routes/views/authentication. |
| Linux workspace | Linux `6.1.158` x86_64, Python `3.11.2`, Node `22.22.3`; no `dotnet`, `csc`, `mono`, `msbuild`, SQL Server, IIS, or local .NET SDK/package cache was found. |
| Existing test registrations | `TWF.Tests/Program.cs` registers **47** cases (`TEST-01`…`TEST-47`), not 48 as stated in `docs/18`; `T5Web.Tests/Program.cs` registers **44** (`T01`…`T44`). TWF tests are **not** built/run by existing Windows CI. |

Build information for **each new** .NET 10 app (SDK versions, offline feed hashes, Debug/Release build, executable test suite, IIS hosting bundle, SQL version) is **pending**. `NEEDS-INFRA`: no local .NET 10 SDK and no verified offline NuGet feed/hosting bundle. Windows CI can prove a scoped build but does not prove an offline installation at the destination.

The `TWF 4.28` attendance and leave-calculation binaries are byte-identical to the T5Web 3.5.7 pinned binaries used by the current TWF rewrite (`src/TWF/engine/engine-pin.json`); the T5V3 3.9.2 desktop binaries differ and cannot be substituted on signature matching alone. No numeric end-to-end equivalence was claimed.

## SHA-256 of binary reference inputs (computed locally, no modification)

| Binary | SHA-256 | CLI metadata / architecture |
| --- | --- | --- |
| `TWF 4.28/App/bin/MyTime3.dll` | `edba04ccafb9f111870f9050f86e9059e92389188bbd3115678afb21691ddd6b` | assembly `MyTime3` 4.27.0.0, IL-only; folder label 4.28 is **not** its assembly version |
| `TWF 4.28/App/bin/TD5.dll` | `3ddbe0f47de8938d1c8c53c407e4f0626dd24f928bcef2e54a7bdc75aab17fa4` | `TD5` 1.0.0.0, IL-only |
| `TWF 4.28/App/bin/T5V3.Attendance.dll` and `T5Web [3.5.7]/App/bin/T5V3.Attendance.dll` | `2a2abd09745ec0a80dff0343b0e1f97e1ed52b8806f2ec61e64da1aa8100d3b7` | `T5V3.Attendance` 1.0.0.0, `32BITREQUIRED` |
| `TWF 4.28/App/bin/T5V3.LeaveCalcs.dll` and `T5Web [3.5.7]/App/bin/T5V3.LeaveCalcs.dll` | `bf193062cc8cc312e1fb703a3bba180e53d6d4e30057c3b1715df4d007d830f6` | `T5V3.LeaveCalcs` 1.0.0.0, `32BITREQUIRED` |
| `T5Web [3.5.7]/App/bin/T5V3Web.dll` | `96c71335ca080f4fa568e65f81957590ffdb953a9fe133ed0000b416afc7e755` | `T5V3Web` 3.5.7.0, `32BITREQUIRED` |
| `T5Web [3.5.7]/App/bin/Dapper.dll` | `9027cd678ac8ff74f2321516528992d1883887ed749e8f6ecad7276dd55cef31` | `Dapper` 2.0.0.0, IL-only (version alone does not establish package version or net10 compatibility) |
| `T5Web [3.5.7]/App/bin/Personnel.dll` | `c35950d10a42e36b6383f9a0c0a5a2c7f7bea10770686ed5a6f18bc9d519fcd7` | `Personnel` 1.4.0.0, `32BITREQUIRED` |
| `T5V3.9.2/T5V3.exe` | `e4f3cf6fae9ccc2fbbb16df546b960fb1db729378bec6f03cc4e04d13bb6a84f` | desktop `T5V3` 3.9.2.0, `32BITREQUIRED`, supplementary only |

SHA-256 via `hashlib.sha256(Path(...).read_bytes()).hexdigest()` and metadata via `dnfile==0.17.0` (local analysis environment; not a new application dependency). MSBuild baseline warns about x86 references with an AnyCPU project; this does not make a 64-bit Core host compatible with those DLLs.

## TWF raw solution versus diagnostic source build

Windows [CI 40395889978](https://github.com/alirezarahimy-code/IO/actions/runs/40395889978) ran raw `TWF.sln` in Release using a pinned assembly search path: **FAIL** (CS0234/CS0246). TWF.Web.csproj lacks at least `System.Data`, `Dapper`, `System.Web.Http`, `System.Web.Optimization`; its engine HintPaths are too shallow by one `..`. The raw solution must not be described as compiling. Architecture warnings (MSB3270, x86 engines vs AnyCPU project) are real; they are not equivalent to compiler errors.

`src/net10/tools/test-twf-shadow.ps1` is an *additional* experiment: copy `src/TWF/` to Windows runner temp, explicitly add missing Framework and pinned non-engine reference metadata (and the original TWF CaptchaMvc library), include the existing but unlisted adapter source, then compile the copied solution and execute all 47 tests from an output under the actual source root. It never edits the tracked baseline. The source bytes and test logic are identical, but **the project metadata differs**. Any pass proves a source/test baseline only, not a passing raw project or a Core web app. Record its actual run result before using it as evidence.
