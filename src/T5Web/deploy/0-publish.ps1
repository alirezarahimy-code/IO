#requires -Version 5.1
<#
.SYNOPSIS
    Build the admin console and lay it out as a folder IIS can serve.

.DESCRIPTION
    A web application folder is not the source folder: the source has .cs files, an
    obj\ and a bin\Release the site must not serve, and the site needs Views\ and
    Content\ next to a bin\ that contains the compiled assemblies.

    This script does exactly that, and nothing else - no site, no app pool, no
    Web.config edit. Those belong to 3-deploy-portal.ps1, so a re-publish does not
    disturb a running site until the next deployment.

    Deliberately not a "web deploy" package: the target machines are often
    air-gapped, and msbuild /p:DeployOnBuild needs Web Deploy installed on both
    ends. Copying files is something you can inspect, diff and zip.

.EXAMPLE
    .\0-publish.ps1 -OutputPath C:\inetpub\T5WebAdmin -Build
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$Build,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'T5Web.Web'
$solution = Join-Path $repoRoot 'T5Web.sln'

if (-not (Test-Path (Join-Path $webRoot 'Web.config'))) {
    throw "T5Web.Web\Web.config not found under $repoRoot - run this from the repository (deploy\0-publish.ps1)."
}

# ------------------------------------------------------------------ build
if ($Build) {
    # Four candidate locations in a fixed order would be a guess, and guessing wrong is
    # how this script first failed on CI: the .NET Framework MSBuild at
    # %WINDIR%\Microsoft.NET\Framework64\v4.0.30319 answered, and its csc.exe rejected
    # <LangVersion>7.3</LangVersion> with CS1617. Ask vswhere for the newest real
    # installation instead, and only fall back to the framework build with a warning.
    $msbuild = $null
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
                    -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
        if ($found -and (Test-Path $found)) { $msbuild = $found }
    }
    if (-not $msbuild) {
        $onPath = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($onPath) { $msbuild = $onPath.Source }
    }
    if (-not $msbuild) {
        $framework = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
        if (Test-Path $framework) {
            $msbuild = $framework
            Write-Warning ("using the .NET Framework MSBuild at {0}; it cannot compile " -f $framework +
                           'LangVersion 7.3. Install Build Tools or the VS IDE for a real build.')
        }
    }
    if (-not $msbuild) {
        throw 'MSBuild not found. Install the .NET Framework 4.7.2 targeting pack or Build Tools, or build first and omit -Build.'
    }
    Write-Host ("  msbuild  {0}" -f $msbuild)
    Write-Host ("  building {0} ({1})" -f $solution, $Configuration)
    # Capture the compiler's own words: "MSBuild failed with exit code 1" is not a
    # diagnosis, and the caller may only be able to show one line of the exception.
    $output = & $msbuild $solution "/p:Configuration=$Configuration" /m /v:minimal /nologo 2>&1
    $code = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) {
        $reasons = @($output | Where-Object { $_ -match ': error ' } | Select-Object -First 5)
        if ($reasons.Count -eq 0) { $reasons = @($output | Select-Object -Last 5) }
        throw ("MSBuild failed with exit code {0}: {1}" -f $code, ($reasons -join ' | '))
    }
}

# A web application project writes into bin\ (that is where IIS looks), so the
# assemblies may be there directly or under bin\<Configuration> - accept both, but
# never guess silently: the chosen folder is printed.
$binCandidates = @((Join-Path $webRoot ("bin\" + $Configuration)), (Join-Path $webRoot 'bin'))
$binSource = $binCandidates | Where-Object { Test-Path (Join-Path $_ 'T5Web.Admin.dll') } | Select-Object -First 1
if (-not $binSource) {
    foreach ($candidate in $binCandidates) {
        if (Test-Path $candidate) { $binSource = $candidate; break }
    }
}
if (-not $binSource) {
    throw ("No build output under {0}. Run with -Build, or build the solution first." -f
           (Join-Path $webRoot 'bin'))
}

# ------------------------------------------------------------------ layout
if (Test-Path $OutputPath) {
    # Refuse to delete a folder that does not look like a previous publish: -Force is
    # for a re-publish, not for a typo in -OutputPath.
    $looksPublished = (Test-Path (Join-Path $OutputPath 'Web.config')) -or
                      ((Get-ChildItem -LiteralPath $OutputPath -Force | Measure-Object).Count -eq 0)
    if (-not $looksPublished -and -not $Force) {
        throw ("$OutputPath exists and does not look like a previous publish. " +
               'Pass -Force only if you are sure it may be replaced.')
    }
    Write-Host ("  clearing {0}" -f $OutputPath) -ForegroundColor DarkYellow
    Get-ChildItem -LiteralPath $OutputPath -Force | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# What a site needs. Everything else in the source folder (.cs, .csproj, obj) stays out.
$files = @('Web.config', 'Global.asax')
$folders = @('Views', 'Content')
foreach ($file in $files) {
    $source = Join-Path $webRoot $file
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination $OutputPath -Force
        Write-Host ("  {0}" -f $file)
    }
}
# Source files are copied out, not in: Content\ holds AriaAdminBranding.cs, which is
# compiled (T30) and must not reach the server. The check below is the proof, this is
# the policy - and Web.config denies those extensions as a third layer.
$skipSource = { param($file) $file.Extension -in @('.cs', '.csproj', '.sln', '.user', '.pdb') }
foreach ($folder in $folders) {
    $source = Join-Path $webRoot $folder
    if (-not (Test-Path $source)) { continue }
    $target = Join-Path $OutputPath $folder
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Recurse -File |
        Where-Object { -not (& $skipSource $_) } |
        ForEach-Object {
            $relative = $_.FullName.Substring($source.Length).TrimStart('\')
            $destination = Join-Path $target $relative
            $parent = Split-Path -Parent $destination
            if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
    $kept = @(Get-ChildItem -LiteralPath $target -Recurse -File).Count
    Write-Host ("  {0}\ : {1} file(s)" -f $folder, $kept)
}

$binOut = Join-Path $OutputPath 'bin'
New-Item -ItemType Directory -Path $binOut -Force | Out-Null
Get-ChildItem -LiteralPath $binSource -Filter *.dll |
    Copy-Item -Destination $binOut -Force
Get-ChildItem -LiteralPath $binSource -Filter *.pdb -ErrorAction SilentlyContinue |
    Copy-Item -Destination $binOut -Force

$assemblies = Get-ChildItem -LiteralPath $binOut -Filter *.dll
Write-Host ("  bin\ : {0} assembl(ies)" -f $assemblies.Count)

# A .cs file in a published folder is source code on the web server; so is an obj\.
$leaks = @(Get-ChildItem -LiteralPath $OutputPath -Recurse -Force |
           Where-Object { $_.Extension -in @('.cs', '.csproj', '.sln', '.user') -or $_.Name -eq 'obj' })
if ($leaks.Count -gt 0) {
    throw ("the published folder contains source: {0}" -f (($leaks | Select-Object -First 5 |
             ForEach-Object { $_.FullName }) -join ', '))
}

# Views\Web.config is not optional: without it a Razor page compiles against
# System.Web.WebPages.WebPage instead of System.Web.Mvc.WebViewPage, and the first
# request to any screen answers 500 "CS0103: The name 'model' does not exist"
# (BUG-38). The same file has to be in the *published* folder, which is what this
# list checks - a source-tree-only config is the bug, not the fix.
# The site's configuration names these assemblies (Views/Web.config resolves the Razor
# host factory by version, Web.config registers EF's SqlClient provider), and a server
# that never had MVC has no GAC copy to fall back on. Each one missing is a different
# 500 on the first request, so the publish refuses the folder instead.
$requiredAssemblies = @(
    'System.Web.Mvc',            # Views/Web.config: the Razor host factory
    'System.Web.WebPages',       # what a .cshtml file compiles against
    'System.Web.WebPages.Razor',
    'System.Web.Razor',
    'EntityFramework',           # the engine's Database First context
    'EntityFramework.SqlServer', # the provider Web.config registers by name
    'Personnel',                 # the pinned engine itself
    'Dapper'
)
foreach ($assembly in $requiredAssemblies) {
    if (-not (Test-Path (Join-Path $OutputPath ("bin\" + $assembly + ".dll")))) {
        throw ("the published folder has no bin\{0}.dll. The site's configuration names it " -f $assembly) +
              " (Views\Web.config or Web.config) and IIS has no GAC copy on a clean server."
    }
}

foreach ($required in @('Web.config', 'bin\T5Web.Admin.dll', 'Views', 'Views\Web.config')) {
    if (-not (Test-Path (Join-Path $OutputPath $required))) {
        throw "the published folder is missing $required."
    }
}

Write-Host ''
Write-Host ("Published to {0}" -f $OutputPath) -ForegroundColor Green
Write-Host ("Next: .\1-install-iis.ps1  then  .\3-deploy-portal.ps1 -SitePath '{0}'" -f $OutputPath)
exit 0
