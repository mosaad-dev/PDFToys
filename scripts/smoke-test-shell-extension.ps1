#Requires -Version 7.0
param([string]$PackageDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $root 'artifacts\package' }
Add-Type -Path (Join-Path $root 'tests\ReleaseSmoke\NativeShellSmoke.cs')
$checks = [NativeShellSmoke]::Run((Join-Path $PackageDirectory 'PDFToys.ShellExtension.dll'))
$version = ([IO.File]::ReadAllText((Join-Path $PackageDirectory 'VERSION'))).Trim()
$resultDirectory = Join-Path $root "artifacts\releases\$version"
New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
[ordered]@{ verifiedUtc = [DateTime]::UtcNow.ToString('O'); passed = @($checks); registrationScope = 'Registration exports resolved only. Elevated installer registration and lifecycle require a clean Windows test machine.' } |
    ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $resultDirectory 'native-smoke-results.json') -Encoding UTF8
Write-Host 'Native shell loading, COM activation and unload smoke checks passed.'
