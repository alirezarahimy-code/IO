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
