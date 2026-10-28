#Requires -Version 7.0
param(
    [string]$IsccPath = "",
    [string]$MsBuildPath = "",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = ([IO.File]::ReadAllText((Join-Path $root 'VERSION'))).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must contain major.minor.patch.' }
$releaseDirectory = Join-Path $root "artifacts\releases\$version"
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null

Push-Location $root
try
{
    # Real Office tests exercise this apphost, so build it before test discovery.
    dotnet build 'src/PDFToys.App/PDFToys.App.csproj' -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release app build failed.' }
    if (!$SkipTests)
    {
        foreach ($project in @('tests/PDFToys.Core.Tests/PDFToys.Core.Tests.csproj','tests/PDFToys.App.Tests/PDFToys.App.Tests.csproj'))
        {
            $testName = [IO.Path]::GetFileNameWithoutExtension($project)
            dotnet test $project -c Release --logger "trx;LogFileName=$testName.trx" --results-directory (Join-Path $releaseDirectory 'test-results')
            if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
        }
    }

    & (Join-Path $PSScriptRoot 'smoke-test-layout.ps1')
    & (Join-Path $PSScriptRoot 'publish.ps1')
    & (Join-Path $PSScriptRoot 'build-shell-extension.ps1') -MsBuildPath $MsBuildPath
    & (Join-Path $PSScriptRoot 'package-layout.ps1')
    $smokeReportPath = & (Join-Path $PSScriptRoot 'smoke-test-release.ps1')
    Copy-Item -LiteralPath $smokeReportPath -Destination (Join-Path $releaseDirectory 'smoke-results.json') -Force
    & (Join-Path $PSScriptRoot 'smoke-test-shell-extension.ps1')

    if ([string]::IsNullOrWhiteSpace($IsccPath))
    {
        $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($isccCommand) { $IsccPath = $isccCommand.Source }
        else
        {
            $candidates = @(
                (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
                (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
                (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
            )
            $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
    }
    if ([string]::IsNullOrWhiteSpace($IsccPath)) { throw 'Inno Setup 6 ISCC.exe is required; supply -IsccPath.' }
    & $IsccPath '/Q' (Join-Path $root 'installer\PDFToys.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $root "artifacts\installer\PDFToys-$version-setup.exe"
    Copy-Item -LiteralPath $installer -Destination $releaseDirectory -Force

    $archive = Join-Path $releaseDirectory "PDFToys-$version-win-x64.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $root 'artifacts\package\*') -DestinationPath $archive -CompressionLevel Optimal

    $files = Get-ChildItem -LiteralPath $releaseDirectory -File | Where-Object { $_.Extension -in @('.exe','.zip') }
    $hashLines = foreach ($file in $files) { "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $file.Name }
    [IO.File]::WriteAllLines((Join-Path $releaseDirectory 'SHA256SUMS.txt'), [string[]]$hashLines, [Text.UTF8Encoding]::new($false))
    $manifest = [ordered]@{
        version = $version
        builtUtc = [DateTime]::UtcNow.ToString('O')
        commit = (git rev-parse HEAD)
        runtime = 'win-x64; .NET 10; self-contained'
        testsRunByScript = !$SkipTests
        tests = @(Get-ChildItem -LiteralPath (Join-Path $releaseDirectory 'test-results') -Filter 'PDFToys.*.trx' -File -ErrorAction SilentlyContinue | ForEach-Object {
            [xml]$testRun = Get-Content -LiteralPath $_.FullName -Raw
            [ordered]@{ report = $_.Name; total = [int]$testRun.TestRun.ResultSummary.Counters.total; passed = [int]$testRun.TestRun.ResultSummary.Counters.passed; failed = [int]$testRun.TestRun.ResultSummary.Counters.failed; skipped = @($testRun.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'NotExecuted' }).Count }
        })
        signing = (Get-AuthenticodeSignature -LiteralPath (Join-Path $releaseDirectory "PDFToys-$version-setup.exe")).Status.ToString()
        files = @($files | ForEach-Object { [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() } })
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseDirectory 'release-manifest.json') -Encoding UTF8
    Write-Host "Release candidate ready at $releaseDirectory"
}
finally { Pop-Location }
