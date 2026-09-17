
$ErrorActionPreference = "Stop"

function Write-Status($message, $type = "Info") {
    switch ($type) {
        "Info"    { Write-Host "  [INFO] $message" -ForegroundColor Magenta }
        "Success" { Write-Host "  [OK]   $message" -ForegroundColor Green }
        "Warning" { Write-Host "  [WARN] $message" -ForegroundColor Yellow }
        "Error"   { Write-Host "  [ERR]  $message" -ForegroundColor Red }
    }
}

function Write-Banner {
    $cat = @'
                                 /\_/\
                                ( -.- )
                                 > ^ <
'@
    Write-Host ""
    Write-Host "  +============================================+" -ForegroundColor DarkMagenta
    Write-Host "  |                                            |" -ForegroundColor DarkMagenta
    Write-Host "  |                fort.uwp                    |" -ForegroundColor Magenta
    Write-Host "  |                Installer                   |" -ForegroundColor DarkMagenta
    Write-Host "  |                                            |" -ForegroundColor DarkMagenta
    Write-Host "  +============================================+" -ForegroundColor DarkMagenta
    Write-Host $cat -ForegroundColor Magenta
    Write-Host ""
}

function Write-Section($title) {
    Write-Host ""
    Write-Host "  -- $title " -ForegroundColor DarkMagenta -NoNewline
    Write-Host ("-" * [Math]::Max(1, 40 - $title.Length)) -ForegroundColor DarkMagenta
}

Write-Banner

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Status "Restarting as Administrator..." "Warning"
    Start-Process PowerShell -Verb RunAs -ArgumentList "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

Write-Status "Running with Administrator privileges" "Success"

Write-Section "Windows Version"
$osVersion = [System.Environment]::OSVersion.Version
$minVersion = [Version]"10.0.17763"  # Windows 10 version 1809

if ($osVersion -lt $minVersion) {
    Write-Status "Windows 10 version 1809 (build 17763) or later is required." "Error"
    Write-Status "Your version: $($osVersion.ToString())" "Error"
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Status "Windows version $($osVersion.Build) meets requirements" "Success"

Write-Section "Sideloading Settings"

$devModeEnabled = $false
$sideloadEnabled = $false

try {
    $devModeKey = Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction SilentlyContinue
    if ($devModeKey) {
        $devModeEnabled = ($devModeKey.AllowDevelopmentWithoutDevLicense -eq 1)
        $sideloadEnabled = ($devModeKey.AllowAllTrustedApps -eq 1)
    }
} catch {
}

if ($devModeEnabled) {
    Write-Status "Developer Mode is enabled" "Success"
} elseif ($sideloadEnabled) {
    Write-Status "Sideloading is enabled" "Success"
} else {
    Write-Status "Sideloading is not enabled. Attempting to enable..." "Warning"
    try {
        $regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
        if (-not (Test-Path $regPath)) {
            New-Item -Path $regPath -Force | Out-Null
        }
        Set-ItemProperty -Path $regPath -Name "AllowAllTrustedApps" -Value 1 -Type DWord -Force
        Write-Status "Sideloading has been enabled" "Success"
    } catch {
        Write-Status "Could not enable sideloading automatically." "Error"
        Write-Status "Please enable 'Developer Mode' or 'Sideload apps' in:" "Warning"
        Write-Status "Settings > Update & Security > For developers" "Warning"
        $continue = Read-Host "Continue anyway? (y/N)"
        if ($continue -ne "y" -and $continue -ne "Y") {
            exit 1
        }
    }
}

Write-Section "Visual C++ Runtime (VCLibs)"

$arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
$vclibsPackage = Get-AppxPackage -Name "Microsoft.VCLibs.140.00" | Where-Object { $_.Architecture -eq $arch }

if ($vclibsPackage) {
    Write-Status "VCLibs $($vclibsPackage.Version) ($arch) is installed" "Success"
} else {
    Write-Status "VCLibs not found. Downloading and installing..." "Warning"

    $vclibsUrl = if ($arch -eq "x64") {
        "https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx"
    } else {
        "https://aka.ms/Microsoft.VCLibs.x86.14.00.Desktop.appx"
    }

    $vclibsPath = "$env:TEMP\VCLibs.appx"

    try {
        Write-Status "Downloading VCLibs from Microsoft..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $vclibsUrl -OutFile $vclibsPath -UseBasicParsing

        Write-Status "Installing VCLibs..."
        Add-AppxPackage -Path $vclibsPath
        Write-Status "VCLibs installed successfully" "Success"

        Remove-Item $vclibsPath -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Status "Failed to install VCLibs: $($_.Exception.Message)" "Error"
        Write-Status "You may need to install it manually from the Microsoft Store" "Warning"
    }
}

Write-Section "Microsoft.UI.Xaml (WinUI)"

$winuiPackage = Get-AppxPackage -Name "Microsoft.UI.Xaml.2.8" -ErrorAction SilentlyContinue

if ($winuiPackage) {
    Write-Status "Microsoft.UI.Xaml $($winuiPackage.Version) is installed" "Success"
} else {
    Write-Status "Microsoft.UI.Xaml 2.8 not found. Will be installed with the app..." "Info"

    $dependencyPath = Join-Path $PSScriptRoot "Dependencies\$arch"
    if (Test-Path $dependencyPath) {
        Write-Status "Found bundled dependencies, installing..."
        Get-ChildItem -Path $dependencyPath -Filter "*.appx" | ForEach-Object {
            try {
                Write-Status "Installing dependency: $($_.Name)..."
                Add-AppxPackage -Path $_.FullName
                Write-Status "Installed $($_.Name)" "Success"
            } catch {
                Write-Status "Note: $($_.Name) may already be installed or will be handled during app install" "Warning"
            }
        }
    }
}

Write-Section "Signing Certificate"
$certFile = Get-ChildItem -Path $PSScriptRoot -Filter "*.cer" | Select-Object -First 1
if ($certFile) {
    Write-Status "Installing signing certificate..."
    try {
        $certThumbprint = (Get-PfxCertificate -FilePath $certFile.FullName).Thumbprint
        $existingCert = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $certThumbprint }

        if ($existingCert) {
            Write-Status "Certificate is already installed" "Success"
        } else {
            Import-Certificate -FilePath $certFile.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            Write-Status "Certificate installed successfully" "Success"
        }
    } catch {
        Write-Status "Failed to install certificate: $($_.Exception.Message)" "Error"
        $continue = Read-Host "Continue without the app cert? (y/N)"
        if ($continue -ne "y" -and $continue -ne "Y") {
            exit 1
        }
    }
} else {
    Write-Status "No certificate file found. The package may be unsigned." "Warning"
}

Write-Section "Installing Fort.ind UWP"
$msixFile = Get-ChildItem -Path $PSScriptRoot -Filter "*.msix" | Select-Object -First 1
if (-not $msixFile) {
    $msixFile = Get-ChildItem -Path $PSScriptRoot -Filter "*.appx" | Select-Object -First 1
}

if ($msixFile) {
    Write-Status "Installing Fort.ind UWP..."

    $existingApp = Get-AppxPackage -Name "*Fort.ind*" -ErrorAction SilentlyContinue
    if ($existingApp) {
        Write-Status "Upgrading over version $($existingApp.Version) - your settings and favourites are kept" "Info"
    }

    try {
        $installed = $false
        try {
            Add-AppxPackage -Path $msixFile.FullName -ForceApplicationShutdown -ErrorAction Stop
            $installed = $true
        } catch {
            if (-not $existingApp) { throw }

            Write-Status "Retrying as a same-or-lower version upgrade..." "Warning"
            Add-AppxPackage -Path $msixFile.FullName -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop
            $installed = $true
        }

        if (-not $installed) { throw "The package could not be installed." }

        Write-Status "Fort.ind UWP installed successfully!" "Success"
        Write-Host ""
        Write-Host "  +============================================+" -ForegroundColor Magenta
        Write-Host "  |        Installation Complete!  =^..^=       |" -ForegroundColor Magenta
        Write-Host "  +============================================+" -ForegroundColor Magenta
        Write-Host ""
        Write-Host "  Find Fort.uwp in your Start menu!" -ForegroundColor Magenta
        Write-Host "  If you run into any bugs, please open" -ForegroundColor DarkMagenta
        Write-Host "  an issue on the github repo at fort-ind/uwp" -ForegroundColor DarkMagenta
        Write-Host ""
    } catch {
        Write-Status "That's awkward... :( the install failed: $($_.Exception.Message)" "Error"
        Write-Host ""
        Write-Status "Troubleshooting tips:" "Warning"
        Write-Status "1. Make sure Developer Mode is enabled in Windows Settings" "Info"
        Write-Status "2. Try restarting your computer and running this installer again" "Info"
        Write-Status "3. Check if Windows Update has pending updates" "Info"

        if ($existingApp) {
            Write-Host ""
            Write-Status "The installed copy was left alone, so nothing was lost." "Info"
            Write-Status "As a last resort you can uninstall Fort.ind UWP from Settings > Apps" "Info"
            Write-Status "and run this installer again - that WILL erase your app settings." "Warning"
        }
    }
} else {
    Write-Status "seems like the APPX/MSIX package is missing, (did you extract the zip right?)" "Error"
}

Write-Host ""
Read-Host "Press Enter to exit"
