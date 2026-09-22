#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$')]
    [string] $Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Check-Exit([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}
Push-Location $repoRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    Check-Exit 'Read source commit'
    $dirty = & git status --porcelain --untracked-files=normal
    Check-Exit 'Check working tree'
    if ($dirty) { throw 'Commit reviewed changes before publishing. Only a clean Git HEAD can be packaged.' }
    & python tools/check_repository.py
    Check-Exit 'Repository audit'

    $releaseRoot = Join-Path $repoRoot "artifacts/releases/$Version"
    if (Test-Path -LiteralPath $releaseRoot) { throw "Release directory already exists; choose a new version: $releaseRoot" }
    $null = New-Item -ItemType Directory -Path $releaseRoot
    $sourceZip = Join-Path $releaseRoot "VPHelper-$Version-source.zip"
    $sourceRoot = Join-Path $releaseRoot 'source'
    & git archive --format=zip "--output=$sourceZip" HEAD
    Check-Exit 'Export committed source'
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $sourceRoot

    # Work from a new, committed-source snapshot; never package an existing developer bin folder.
    Push-Location $sourceRoot
    try {
        & dotnet test tests/VPHelper.Core.Tests/VPHelper.Core.Tests.csproj -c Release --filter 'Category!=Live' --logger trx --results-directory (Join-Path $releaseRoot 'test-results')
        Check-Exit 'Offline regression tests'
        $packageName = "VPHelper-$Version-win-x64"
        $packageRoot = Join-Path $releaseRoot $packageName
        & dotnet publish src/VPHelper.WinUI/VPHelper.WinUI.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true `
            -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false `
            -p:DebugSymbols=false -p:DebugType=None "-p:Version=$Version" "-p:RepositoryCommit=$commit" `
            -p:ContinuousIntegrationBuild=true -o $packageRoot
        Check-Exit 'Publish Windows x64'
        foreach ($name in @('VPHelper.WinUI.exe', 'VPHelper.WinUI.dll', 'Microsoft.UI.Xaml.dll', 'coreclr.dll', 'WebView2Loader.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $name))) { throw "Required runtime file missing: $name" }
        }
        Copy-Item -LiteralPath 'THIRD_PARTY_NOTICES.md' -Destination $packageRoot
        Copy-Item -LiteralPath 'docs/INSTALL.md' -Destination (Join-Path $packageRoot 'INSTALL.md')
        Copy-Item -LiteralPath 'LICENSES' -Destination $packageRoot -Recurse
        if (Test-Path -LiteralPath 'LICENSE') { Copy-Item -LiteralPath 'LICENSE' -Destination $packageRoot }
        & python tools/collect_package_licenses.py src/VPHelper.WinUI/obj/project.assets.json (Join-Path $packageRoot 'LICENSES/packages')
        Check-Exit 'Collect dependency licenses'
        $sdk = (& dotnet --version).Trim()
        Check-Exit 'Read SDK version'
        [ordered]@{ version = $Version; commit = $commit; rid = 'win-x64'; sdk = $sdk;
            builtAtUtc = [DateTime]::UtcNow.ToString('o'); selfContained = $true;
            webView2 = 'Evergreen Runtime required separately'; codeSigned = $false } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'release-manifest.json') -Encoding utf8
        $forbidden = Get-ChildItem -LiteralPath $packageRoot -Recurse -Force | Where-Object {
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint -or
            $_.Name -in @('data', 'WebView2', 'EBWebView', '.git', 'tests', 'fixtures', 'bin', 'obj') -or
            $_.Name -match '\.(db(?:-wal|-shm)?|sqlite3?|pfx|p12|log)$'
        }
        if ($forbidden) { throw 'Publish output contains local data, a reparse point or a forbidden artifact.' }
        $zip = Join-Path $releaseRoot "$packageName.zip"
        Compress-Archive -LiteralPath $packageRoot -DestinationPath $zip -CompressionLevel Optimal
        $hashLines = foreach ($asset in @($zip, $sourceZip)) {
            $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $([IO.Path]::GetFileName($asset))"
        }
        $hashLines | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
        Write-Host "Portable package verified: $zip"
        Write-Host 'Not uploaded, code-signed, or verified on a clean Windows installation.'
    } finally { Pop-Location }
} finally { Pop-Location }
