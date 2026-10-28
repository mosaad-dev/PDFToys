$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\PDFToys.App\PDFToys.App.csproj"
$output = Join-Path $root "artifacts\publish\win-x64"

# This directory belongs to the build. Never merge a new release with old DLLs.
if (Test-Path -LiteralPath $output)
{
    $resolvedOutput = (Resolve-Path -LiteralPath $output).Path
    $expectedOutput = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\publish\win-x64'))
    if (![string]::Equals($resolvedOutput, $expectedOutput, [StringComparison]::OrdinalIgnoreCase))
    {
        throw 'Refusing to clean a publish directory outside artifacts/publish/win-x64.'
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -p:PublishProfile=win-x64-selfcontained `
    -o $output

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published to $output"
