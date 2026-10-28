#Requires -Version 7.0
param([string]$PackageDirectory = "")

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $root 'artifacts\package' }
$executable = Join-Path $PackageDirectory 'PDFToys.App.exe'
if (!(Test-Path -LiteralPath $executable)) { throw "Packaged app not found: $executable" }
$caseDirectory = Join-Path $root ('artifacts\release-qa\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $caseDirectory -Force | Out-Null

function Invoke-PdfToysSmoke([string[]]$Arguments, [int]$ExpectedExitCode = 0)
{
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try
    {
        if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Packaged headless operation timed out.' }
        if ($process.ExitCode -ne $ExpectedExitCode) { throw "Unexpected headless exit code $($process.ExitCode); expected $ExpectedExitCode. Arguments: $($Arguments -join ' ')" }
    }
    finally { $process.Dispose() }
}

$firstDirectory = Join-Path $caseDirectory 'first source'
$secondDirectory = Join-Path $caseDirectory 'second source'
New-Item -ItemType Directory -Path $firstDirectory,$secondDirectory -Force | Out-Null
$first = Join-Path $firstDirectory 'report.txt'
$second = Join-Path $secondDirectory 'report.txt'
[IO.File]::WriteAllText($first, 'FIRST DOCUMENT unique content', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($second, 'SECOND DOCUMENT distinct content', [Text.UTF8Encoding]::new($false))
$firstHash = (Get-FileHash -LiteralPath $first).Hash
$secondHash = (Get-FileHash -LiteralPath $second).Hash

Invoke-PdfToysSmoke @('--contract-version','1','--command','convert-to-pdf','--input',$first)
$single = Get-ChildItem -LiteralPath $firstDirectory -Filter '*_Converted.pdf' -File
if (@($single).Count -ne 1) { throw 'Single document conversion did not produce exactly one output.' }
Invoke-PdfToysSmoke @('--contract-version','1','--command','combine-to-pdf','--input',$first,'--input',$second)
$combined = Get-ChildItem -LiteralPath $firstDirectory -Filter 'combined*.pdf' -File
if (@($combined).Count -ne 1) { throw 'Same-basename combine did not produce exactly one final PDF.' }
Invoke-PdfToysSmoke @('--command','pdf-to-markdown','--input',$combined.FullName)
$markdownDirectory = Get-ChildItem -LiteralPath $firstDirectory -Directory -Filter 'combined*_markdown_*' | Select-Object -First 1
if ($null -eq $markdownDirectory) { throw 'Markdown export directory was not created.' }
$pages = @(Get-ChildItem -LiteralPath $markdownDirectory.FullName -File -Filter '*.md' | Sort-Object Name)
if ($pages.Count -ne 2) { throw "Expected two combined text pages; got $($pages.Count)." }
if ((Get-Content -LiteralPath $pages[0].FullName -Raw) -notmatch 'FIRST DOCUMENT') { throw 'First combined page lost input order/content.' }
if ((Get-Content -LiteralPath $pages[1].FullName -Raw) -notmatch 'SECOND DOCUMENT') { throw 'Second combined page lost input order/content.' }
Invoke-PdfToysSmoke @('--command','pdf-to-png','--input',$combined.FullName)
$pngDirectory = Get-ChildItem -LiteralPath $firstDirectory -Directory -Filter 'combined*_png_*' | Select-Object -First 1
if ($null -eq $pngDirectory) { throw 'PNG export directory was not created.' }
$images = @(Get-ChildItem -LiteralPath $pngDirectory.FullName -File -Filter '*.png')
if ($images.Count -ne 2) { throw "Expected two rendered PNG pages; got $($images.Count)." }
if (($images | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName).Hash } | Sort-Object -Unique | Measure-Object).Count -ne 2) { throw 'Different source documents produced duplicated page images.' }
Invoke-PdfToysSmoke @('--command','convert-to-pdf','--input',$first)
if (@(Get-ChildItem -LiteralPath $firstDirectory -File -Filter '*_Converted*.pdf').Count -ne 2) { throw 'Repeated conversion overwrote an earlier result.' }
if ((Get-FileHash -LiteralPath $first).Hash -ne $firstHash -or (Get-FileHash -LiteralPath $second).Hash -ne $secondHash) { throw 'A source file was changed by the packaged workflow.' }

$officeChecks = foreach ($office in @(
    @{ extension = 'docx'; progId = 'Word.Application'; process = 'WINWORD' },
    @{ extension = 'xlsx'; progId = 'Excel.Application'; process = 'EXCEL' },
    @{ extension = 'pptx'; progId = 'PowerPoint.Application'; process = 'POWERPNT' }
))
{
    $reason = if (![OperatingSystem]::IsWindows()) { 'Windows is required.' }
        elseif ($null -eq [Type]::GetTypeFromProgID($office.progId, $false)) { 'Corresponding desktop Office application is not installed.' }
        elseif (@(Get-Process -Name $office.process -ErrorAction SilentlyContinue).Count -gt 0) { 'An existing Office session is open; it is excluded from smoke automation.' }
    if ($reason) { [ordered]@{ format = $office.extension; outcome = 'skipped'; reason = $reason }; continue }
    $officeInput = Join-Path $caseDirectory ('office.' + $office.extension)
    Copy-Item -LiteralPath (Join-Path $root ('tests\PDFToys.Core.Tests\Fixtures\Office\sample.' + $office.extension)) -Destination $officeInput
    $officeSourceHash = (Get-FileHash -LiteralPath $officeInput).Hash
    Invoke-PdfToysSmoke @('--command','to-pdf','--input',$officeInput)
    # Each input gets the same stem; unique names must prevent Office result collisions too.
    if ((Get-FileHash -LiteralPath $officeInput).Hash -ne $officeSourceHash) { throw 'Office conversion changed its source document.' }
    [ordered]@{ format = $office.extension; outcome = 'passed'; reason = 'Real packaged isolated worker returned a validated PDF.' }
}
if (@($officeChecks | Where-Object { $_.outcome -eq 'passed' }).Count -ne @(Get-ChildItem -LiteralPath $caseDirectory -File -Filter 'office_Converted*.pdf').Count) { throw 'Office smoke output manifest does not match successful checks.' }

$report = [ordered]@{
    version = ([IO.File]::ReadAllText((Join-Path $PackageDirectory 'VERSION'))).Trim()
    verifiedUtc = [DateTime]::UtcNow.ToString('O')
    executable = $executable
    caseDirectory = $caseDirectory
    passed = @('self-contained startup','text-to-PDF conversion','same-basename combine and input order','Markdown text extraction','PDFium/Skia PNG rendering','non-overwriting repeated conversion','original source hashes preserved')
    office = @($officeChecks)
}
$reportPath = Join-Path $caseDirectory 'smoke-results.json'
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "Packaged smoke checks passed. Evidence: $caseDirectory"
Write-Output $reportPath
