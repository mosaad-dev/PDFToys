#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([IO.File]::ReadAllText((Join-Path $root 'VERSION'))).Trim()
$output = Join-Path $root "artifacts\releases\$version\visual-qa"
dotnet run --project (Join-Path $root 'tests\PDFToys.VisualSmoke\PDFToys.VisualSmoke.csproj') -c Release -- $output
if ($LASTEXITCODE -ne 0) { throw 'Headless layout/render/focus smoke checks failed.' }
