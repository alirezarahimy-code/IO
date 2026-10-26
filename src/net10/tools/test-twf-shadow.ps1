# Compile every tracked TWF source on a Windows runner, without editing src/TWF/.
# Raw TWF.sln cannot build: missing Web references (CI 40395889978). This diagnostic
# build copies its sources/projects, adds only explicit *build metadata*, then runs
# the real 47-test suite against the original source tree. It is NOT the raw build.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$source = Join-Path $root 'src/TWF'
$shadow = Join-Path $env:RUNNER_TEMP 'twf-framework-reference-shadow'
if (Test-Path -LiteralPath $shadow) { throw "Refusing to overwrite existing shadow: $shadow" }
Copy-Item -LiteralPath $source -Destination $shadow -Recurse
if (-not (Test-Path -LiteralPath (Join-Path $shadow 'TWF.sln'))) {
    throw 'Reference shadow copy is incomplete'
}
$pinned = Join-Path $root 'T5Web [3.5.7]/App/bin'
$captcha = Join-Path $root 'TWF 4.28/App/bin/CaptchaMvc.dll'
if (-not (Test-Path -LiteralPath $captcha)) { throw 'Original TWF CaptchaMvc is missing' }

$addReferences = @{
    'TWF.Web' = @('System.Data', 'System.Configuration', 'System.Drawing',
                  'System.Web.Routing', 'System.Web.Http', 'System.Web.Optimization',
                  'Dapper', 'CaptchaMvc')
    'TWF.Tests' = @('System.Web', 'System.Web.Routing', 'System.Web.Mvc', 'T5V3.Model')
}
$fromPinned = @('System.Web.Http', 'System.Web.Optimization', 'Dapper',
                'System.Web.Mvc', 'T5V3.Model')
$namespace = 'http://schemas.microsoft.com/developer/msbuild/2003'
foreach ($name in @('TWF.Web', 'TWF.Tests')) {
    $projectPath = Join-Path $shadow "$name/$name.csproj"
    [xml]$xml = Get-Content -Raw -LiteralPath $projectPath
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('m', $namespace)
    $group = $xml.CreateElement('ItemGroup', $namespace)
    foreach ($referenceName in $addReferences[$name]) {
        $existing = @($xml.SelectNodes('//m:Reference', $ns) | ForEach-Object { $_.GetAttribute('Include') })
        if ($existing -contains $referenceName) { throw "Unexpected duplicate $referenceName in $name" }
        $reference = $xml.CreateElement('Reference', $namespace)
        $reference.SetAttribute('Include', $referenceName)
        $hint = ''
        if ($referenceName -eq 'CaptchaMvc') { $hint = $captcha }
        elseif ($fromPinned -contains $referenceName) {
            $hint = Join-Path $pinned "$referenceName.dll"
        }
        if ($hint) {
            if (-not (Test-Path -LiteralPath $hint)) { throw "Missing declared reference: $hint" }
            $hintNode = $xml.CreateElement('HintPath', $namespace)
            $hintNode.InnerText = $hint
            [void]$reference.AppendChild($hintNode)
        }
        [void]$group.AppendChild($reference)
        Write-Host "SHADOW ONLY: $name adds reference $referenceName"
    }
    [void]$xml.DocumentElement.AppendChild($group)
    if ($name -eq 'TWF.Tests') {
        # Test Program.cs directly names TD5 model types; transitive Framework
        # project references do not automatically supply compiler references.
        $refGroup = $xml.CreateElement('ItemGroup', $namespace)
        $ref = $xml.CreateElement('ProjectReference', $namespace)
        $ref.SetAttribute('Include', '..\TD5\TD5.csproj')
        [void]$refGroup.AppendChild($ref)
        [void]$xml.DocumentElement.AppendChild($refGroup)
        Write-Host 'SHADOW ONLY: TWF.Tests adds direct TD5 project reference'
    }
    $xml.Save($projectPath)
}

# The source file exists but TWF.Infrastructure.csproj does not compile it. Include
# it in the shadow (not the original) so source completeness is actually tested.
$infraPath = Join-Path $shadow 'TWF.Infrastructure/TWF.Infrastructure.csproj'
[xml]$infra = Get-Content -Raw -LiteralPath $infraPath
if (-not (Test-Path -LiteralPath (Join-Path $shadow 'TWF.Infrastructure/Adapters/T5V3BackendAdapter.cs'))) {
    throw 'T5V3BackendAdapter source missing'
}
$compileGroup = $infra.CreateElement('ItemGroup', $namespace)
$compile = $infra.CreateElement('Compile', $namespace)
$compile.SetAttribute('Include', 'Adapters\T5V3BackendAdapter.cs')
[void]$compileGroup.AppendChild($compile)
[void]$infra.DocumentElement.AppendChild($compileGroup)
$infra.Save($infraPath)
Write-Host 'SHADOW ONLY: TWF.Infrastructure includes existing T5V3BackendAdapter.cs'

Write-Host 'Original src/TWF project/source bytes were not changed. Shadow build is not the raw baseline.'
& (Join-Path $PSScriptRoot 'test-twf-baseline.ps1') -SolutionRoot $shadow
exit $LASTEXITCODE
