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
    $msbuild = @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "$env:ProgramFiles (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $msbuild) {
        $onPath = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($onPath) { $msbuild = $onPath.Source }
    }
    if (-not $msbuild) {
        throw 'MSBuild not found. Install the .NET Framework 4.7.2 targeting pack or Build Tools, or build first and omit -Build.'
    }
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

$binSource = Join-Path $webRoot ("bin\" + $Configuration)
if (-not (Test-Path $binSource)) {
    throw "No build output at $binSource. Run with -Build, or build the solution first."
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
foreach ($folder in $folders) {
    $source = Join-Path $webRoot $folder
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination $OutputPath -Recurse -Force
        Write-Host ("  {0}\" -f $folder)
    }
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

foreach ($required in @('Web.config', 'bin\T5Web.Web.dll', 'Views')) {
    if (-not (Test-Path (Join-Path $OutputPath $required))) {
        throw "the published folder is missing $required."
    }
}

Write-Host ''
Write-Host ("Published to {0}" -f $OutputPath) -ForegroundColor Green
Write-Host ("Next: .\1-install-iis.ps1  then  .\3-deploy-portal.ps1 -SitePath '{0}'" -f $OutputPath)
exit 0
