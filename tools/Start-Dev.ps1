#requires -Version 7.0
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appDir = Join-Path $repoRoot "src/VPHelper.WinUI/bin/x64/$Configuration/net8.0-windows10.0.19041.0/win-x64"
$assembly = Join-Path $appDir 'VPHelper.WinUI.dll'
$project = Join-Path $repoRoot 'src/VPHelper.WinUI/VPHelper.WinUI.csproj'
if (-not (Test-Path -LiteralPath $assembly)) {
    throw 'Build VPHelper.WinUI for x64 first. See README.md for the development build command.'
}
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
Push-Location $repoRoot
try {
    # Prefer the SDK pinned by global.json. Existing net8 builds may also be launched with SDK 8.
    & $dotnet --version 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Set-Location ([System.IO.Path]::GetPathRoot($repoRoot))
        $sdkVersion = & $dotnet --version
        if ($LASTEXITCODE -ne 0) { throw 'Install a .NET SDK to use the development launcher.' }
        Write-Host "Launching the existing build with .NET SDK $sdkVersion (no rebuild)."
    }
    # Standard SDK launch: no trust-zone edits, certificates or security setting changes.
    $appExe = Join-Path $appDir 'VPHelper.WinUI.exe'
    & $dotnet msbuild $project -target:Run -property:Platform=x64 "-property:Configuration=$Configuration" "-property:RunCommand=%22$appExe%22" -verbosity:quiet
    exit $LASTEXITCODE
}
finally { Pop-Location }
