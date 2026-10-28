$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish\win-x64"
$shellDir = Join-Path $root "artifacts\shell-extension"
$packageDir = Join-Path $root "artifacts\package"
$packageScriptsDir = Join-Path $packageDir "scripts"

if (Test-Path -LiteralPath $packageDir)
{
    $resolvedPackage = (Resolve-Path -LiteralPath $packageDir).Path
    $expectedPackage = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\package'))
    if (![string]::Equals($resolvedPackage, $expectedPackage, [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'Refusing to clean a package directory outside the expected artifacts/package location.'
    }
    Remove-Item -LiteralPath $resolvedPackage -Recurse -Force
}

if (-not (Test-Path $publishDir))
{
    throw "Publish output not found. Run scripts\publish.ps1 first."
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $packageDir -Recurse -Force

$shellDll = Join-Path $shellDir "PDFToys.ShellExtension.dll"
if (-not (Test-Path -LiteralPath $shellDll -PathType Leaf))
{
    throw "Shell extension DLL not found. Run scripts\build-shell-extension.ps1 first."
}

Copy-Item -LiteralPath $shellDll -Destination $packageDir -Force

New-Item -ItemType Directory -Force -Path $packageScriptsDir | Out-Null
Copy-Item (Join-Path $PSScriptRoot "register-shell-extension.ps1") $packageScriptsDir -Force

if (Test-Path (Join-Path $root "NOTICE"))
{
    Copy-Item (Join-Path $root "NOTICE") $packageDir -Force
}

if (Test-Path (Join-Path $root "LICENSE"))
{
    Copy-Item (Join-Path $root "LICENSE") $packageDir -Force
}

Copy-Item -LiteralPath (Join-Path $root 'VERSION') -Destination $packageDir -Force
& (Join-Path $PSScriptRoot 'collect-notices.ps1') -PackageDirectory $packageDir
foreach ($required in @('PDFToys.App.exe','PDFToys.Core.dll','PDFToys.ShellExtension.dll','pdfium.dll','libSkiaSharp.dll','coreclr.dll','LICENSE','NOTICE'))
{
    if (!(Test-Path -LiteralPath (Join-Path $packageDir $required))) { throw "Required release file is missing: $required" }
}
# Symbols are useful to developers but need not ship to every user.
Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

Write-Host "Package layout created at $packageDir"
