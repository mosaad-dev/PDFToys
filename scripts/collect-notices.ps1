param([string]$PackageDirectory = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) { $PackageDirectory = Join-Path $root 'artifacts\package' }
$assets = Get-Content -LiteralPath (Join-Path $root 'src\PDFToys.App\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$dependencies = Get-Content -LiteralPath (Join-Path $PackageDirectory 'PDFToys.App.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$noticeDirectory = Join-Path $PackageDirectory 'licenses'
New-Item -ItemType Directory -Force -Path $noticeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'third-party\PDFium-147.0.7690') -Destination $noticeDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'third-party\Inter') -Destination $noticeDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'third-party\license-texts') -Destination $noticeDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $noticeDirectory 'license-texts\Apache-2.0.txt') -Force

$inventory = foreach ($library in $dependencies.libraries.GetEnumerator() | Sort-Object Key)
{
    if ($library.Value.type -ne 'package' -and !$library.Key.StartsWith('runtimepack.')) { continue }
    $identity = $library.Key -replace '^runtimepack\.', ''
    $relativePath = $identity.ToLowerInvariant()
    if ($assets.libraries.ContainsKey($identity)) { $relativePath = $assets.libraries[$identity].path }
    $packagePath = $null
    foreach ($folder in $assets.packageFolders.Keys)
    {
        $candidate = Join-Path $folder $relativePath
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packagePath = $candidate; break }
    }
    if (!$packagePath) { throw "Cannot collect notices for restored package $identity." }
    $nuspec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' -File | Select-Object -First 1
    if (!$nuspec) { throw "Missing NuGet metadata for $identity." }
    [xml]$specification = Get-Content -LiteralPath $nuspec.FullName -Raw
    $metadata = $specification.package.metadata
    $destination = Join-Path $noticeDirectory ($identity -replace '/', '-')
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $licenseFiles = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES|COPYING|COPYRIGHT)(\..*)?$' })
    foreach ($file in $licenseFiles) { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force }
    if ($metadata.license.type -eq 'file')
    {
        $declaredLicense = [IO.Path]::GetFullPath((Join-Path $packagePath $metadata.license.InnerText))
        if (!$declaredLicense.StartsWith(([IO.Path]::GetFullPath($packagePath) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid license path for $identity." }
        Copy-Item -LiteralPath $declaredLicense -Destination $destination -Force
    }
    $attribution = @(
        "Package: $identity",
        "Authors: $($metadata.authors)",
        "Copyright: $($metadata.copyright)",
        "License: $($metadata.license.InnerText)",
        "Project: $($metadata.projectUrl)",
        "License URL: $($metadata.licenseUrl)",
        '',
        'Package-supplied notices are retained alongside this file.',
        'For expression-only packages, see ../license-texts and this attribution.'
    )
    [IO.File]::WriteAllLines((Join-Path $destination 'ATTRIBUTION.txt'), [string[]]$attribution, [Text.UTF8Encoding]::new($false))
    [ordered]@{ package = $identity; authors = [string]$metadata.authors; copyright = [string]$metadata.copyright; license = [string]$metadata.license.InnerText; project = [string]$metadata.projectUrl }
}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $noticeDirectory 'packages.json') -Encoding UTF8
foreach ($required in @('PDFium-147.0.7690\licenses\pdfium.txt','Inter\LICENSE.txt'))
{
    if (!(Test-Path -LiteralPath (Join-Path $noticeDirectory $required))) { throw "Required third-party notice is missing: $required" }
}
$skiaNotice = Get-ChildItem -LiteralPath $noticeDirectory -Directory -Filter 'SkiaSharp.NativeAssets.Win32-*' | ForEach-Object { Join-Path $_.FullName 'THIRD-PARTY-NOTICES.txt' }
if (!$skiaNotice -or !(Test-Path -LiteralPath $skiaNotice)) { throw 'The Skia native redistribution notices are missing.' }
Write-Host "Collected third-party notices for $(@($inventory).Count) packages."
