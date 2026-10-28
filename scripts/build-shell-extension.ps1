param([string]$MsBuildPath = "")

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\PDFToys.ShellExtension\PDFToys.ShellExtension.vcxproj"
$output = Join-Path $root "artifacts\shell-extension"

if ([string]::IsNullOrWhiteSpace($MsBuildPath))
{
    $buildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($buildCommand) { $MsBuildPath = $buildCommand.Source }
    else
    {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path -LiteralPath $vswhere)
        {
            $MsBuildPath = & $vswhere -latest -version '[17.0,18.0)' -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        }
    }
}
if ([string]::IsNullOrWhiteSpace($MsBuildPath) -or !(Test-Path -LiteralPath $MsBuildPath))
{
    throw "Visual Studio 2022 MSBuild and the C++/ATL tools are required. Supply -MsBuildPath or run from Developer PowerShell."
}

$version = ([IO.File]::ReadAllText((Join-Path $root 'VERSION'))).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain a numeric major.minor.patch version.' }
$versionHeader = "// Generated from VERSION by scripts/build-shell-extension.ps1.`n#define PDFTOYS_FILE_VERSION $($version.Replace('.', ',')),0`n#define PDFTOYS_VERSION_STRING `"$version.0`"`n#define PDFTOYS_PRODUCT_VERSION_STRING `"$version`"`n"
[IO.File]::WriteAllText((Join-Path $root 'src\PDFToys.ShellExtension\PDFToysVersion.h'), $versionHeader, [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path $output | Out-Null

& $MsBuildPath $project /m /p:Configuration=Release /p:Platform=x64 /p:OutDir="$output\" /p:IntDir="$root\artifacts\native-obj\" /verbosity:minimal

if ($LASTEXITCODE -ne 0)
{
    throw "Shell extension build failed with exit code $LASTEXITCODE."
}

Write-Host "Shell extension built to $output"
