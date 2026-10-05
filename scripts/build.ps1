param(
    [ValidateSet('x64', 'x86')] [string] $Platform = 'x64',
    [switch] $Test,
    [switch] $Package,
    [string] $ReferencePath
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $repoRoot '.tools'
$exportRoot = Join-Path $toolsRoot 'DllExport.1.7.4'
# Retain the namespace-patched attribute cache alongside its export tool.
# Pin the exact upstream revision inspected during implementation.
$revision = '5372b75af3650f2e8048a5abffc7119f65669029'
if (-not (Test-Path -LiteralPath (Join-Path $exportRoot 'tools/net.r_eg.DllExport.targets'))) {
    New-Item -ItemType Directory -Force $toolsRoot | Out-Null
    if ($ReferencePath) {
        $source = Join-Path $ReferencePath 'WakaTime/packages/DllExport.1.7.4'
    } else {
        $archive = Join-Path $toolsRoot 'export-buildtools.zip'
        $extract = Join-Path $toolsRoot 'upstream'
        Invoke-WebRequest -Uri "https://github.com/wakatime/notepadpp-wakatime/archive/$revision.zip" -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
        $source = Join-Path $extract "notepadpp-wakatime-$revision/WakaTime/packages/DllExport.1.7.4"
    }
    Copy-Item -LiteralPath $source -Destination $exportRoot -Recurse
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuildPath) { throw 'Install Visual Studio with MSBuild and the .NET Framework 4.8 developer pack.' }
function Invoke-Build([string] $project) {
    & $msbuildPath $project /p:Configuration=Release "/p:Platform=$Platform" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
Invoke-Build (Join-Path $repoRoot 'AwWatcherNPP.csproj')
$dll = Join-Path $repoRoot "bin/$Platform/Release/AwWatcherNPP.dll"
if ($Test) {
    Invoke-Build (Join-Path $repoRoot 'tests/Tests.csproj')
    & (Join-Path $repoRoot "tests/bin/$Platform/AwWatcherNPP.Tests.exe") $dll
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Package) {
    $stage = Join-Path $repoRoot "dist/$Platform/AwWatcherNPP"
    # Recreate this package's staging directory so obsolete files cannot ship.
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedStage.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Package staging directory is outside the repository dist directory.'
    }
    if (Test-Path -LiteralPath $resolvedStage) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item -LiteralPath $dll -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'), (Join-Path $repoRoot 'LICENSE.txt') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $repoRoot 'assets') -Destination $stage -Recurse
    Compress-Archive -Path $stage -DestinationPath (Join-Path $repoRoot "dist/AwWatcherNPP-0.1.0-$Platform.zip") -Force
}
