# PowerShell deployment kit — IIS + SQL Server + the console

Windows Server only. Every script is **idempotent**: running it twice is a no-op,
so a failed run can simply be repeated after the cause is fixed.

Run in this order, from an **elevated** PowerShell, in the repository checkout:

```powershell
cd D:\IO\src\T5Web\deploy

.\0-publish.ps1 -OutputPath C:\inetpub\T5WebAdmin -Build
.\1-install-iis.ps1
.\2-install-sqlserver.ps1 -SetupExe D:\media\SQLServer2019-SSEI-Expr.exe
.\2-install-sqlserver.ps1 -DatabaseScripts ..\db          # second run: portal objects + login
.\3-deploy-portal.ps1 -SitePath C:\inetpub\T5WebAdmin -DataSource 'SRV-SQL01\T5' -Mode coexist -Port 8080
.\4-verify-install.ps1 -BaseUrl http://localhost:8080
```

| Script | What it does | Needs |
| :-- | :-- | :-- |
| `0-publish.ps1` | Builds the solution and lays out a folder IIS can serve: `Web.config`, `Global.asax`, `Views\`, `Content\`, `bin\` with the assemblies — and refuses to publish a folder that still contains `.cs` files | none (MSBuild for `-Build`) |
| `1-install-iis.ps1` | Enables the IIS roles, ASP.NET 4.x, Windows Authentication, request filtering; prints a reboot-required flag | Administrator |
| `2-install-sqlserver.ps1` | Detects an existing instance; optionally installs SQL Server Express silently from local media; creates the two catalogs or restores them from backup; creates the least-privilege login | Administrator, SQL sysadmin |
| `3-deploy-portal.ps1` | App pool (.NET 4.0, always running), site, Windows auth on / anonymous off, folder ACLs for the pool identity, `Web.config` appSettings patched, optional MSBuild | Administrator, MSBuild for `-Build` |
| `4-verify-install.ps1` | The acceptance checklist as code: schema, relations, migration fingerprint, HTTP 401/200, least-privilege proof | sqlcmd, network to the site |

Nothing in this folder fetches anything from the internet, and no password is
stored in any file: the SA password and the `t5web_admin` password are asked for
at run time.

The book that explains *why* each step exists, and what to do when one fails, is
`../docs/13-install-iis-sqlserver.md`.

Compiling and running the 36 tests needs no server of your own: the GitHub Actions
workflow `.github/workflows/windows-build.yml` does it on a real Windows runner,
together with the database scripts on SQL Server Express LocalDB — and a third job
that runs `0-publish.ps1`, `1-install-iis.ps1` and `3-deploy-portal.ps1` on a real IIS
and proves that an anonymous request is refused with 401. See
`../docs/14-windows-ci.md`.
