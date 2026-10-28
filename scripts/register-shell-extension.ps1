param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("register", "unregister")]
    [string]$Action = "register",
    [string]$DllPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DllPath))
{
    $root = Split-Path -Parent $PSScriptRoot
    $DllPath = Join-Path $root "artifacts\shell-extension\PDFToys.ShellExtension.dll"
}

if (-not (Test-Path $DllPath))
{
    throw "Shell extension DLL not found at '$DllPath'. Run scripts\build-shell-extension.ps1 first."
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin)
{
    throw "Registering the shell extension requires an elevated PowerShell session."
}

if ($Action -eq "register")
{
    $process = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
    -ArgumentList "/s `"$DllPath`"" -WindowStyle Hidden -Wait -PassThru

if ($process.ExitCode -ne 0)
{
    throw "Registration failed with exit code $($process.ExitCode)."
}

Write-Host "Registered $DllPath"
}
else
{
    $process = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" `
        -ArgumentList "/s /u `"$DllPath`"" -WindowStyle Hidden -Wait -PassThru

    if ($process.ExitCode -ne 0)
    {
        throw "Unregistration failed with exit code $($process.ExitCode)."
    }

    Write-Host "Unregistered $DllPath"
}
