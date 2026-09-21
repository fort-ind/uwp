
$ErrorActionPreference = "Stop"

$ExpectedPackageName = "fortind.desktopPreview"
$KnownPublishers = @("CN=koold", "CN=fort.ind")

function Get-CanonicalDn($distinguishedName) {
    if ([string]::IsNullOrWhiteSpace($distinguishedName)) { return "" }
    try {
        $dn = New-Object System.Security.Cryptography.X509Certificates.X500DistinguishedName($distinguishedName)
        return $dn.Format($false).Trim()
    } catch {
        return $distinguishedName.Trim()
    }
}

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

function Get-OSArchitecture {
    try {
        $kernel32 = Add-Type -Name "Kernel32" -Namespace "FortIndInstaller" -PassThru -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true)]
public static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
'@
        $processMachine = [uint16]0
        $nativeMachine = [uint16]0
        $handle = [System.Diagnostics.Process]::GetCurrentProcess().Handle
        if ($kernel32::IsWow64Process2($handle, [ref]$processMachine, [ref]$nativeMachine)) {
            switch ($nativeMachine) {
                0x014c { return "x86" }
                0x8664 { return "x64" }
                0xAA64 { return "arm64" }
                { $_ -in 0x01c0, 0x01c2, 0x01c4 } { return "arm" }
            }
        }
    } catch {
    }

    if (${env:ProgramFiles(Arm)}) { return "arm64" }
    if ([Environment]::Is64BitOperatingSystem) { return "x64" }
    return "x86"
}

function Get-PackageIdentity($packagePath) {
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            $entry = $archive.GetEntry("AppxManifest.xml")
            if (-not $entry) { return $null }

            $reader = New-Object System.IO.StreamReader($entry.Open())
            try {
                [xml]$manifest = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }

            $identity = $manifest.DocumentElement.GetElementsByTagName("Identity") | Select-Object -First 1
            if (-not $identity) { return $null }

            return [PSCustomObject]@{
                Name      = $identity.GetAttribute("Name")
                Publisher = $identity.GetAttribute("Publisher")
            }
        } finally {
            $archive.Dispose()
        }
    } catch {
        return $null
    }
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
    Write-Status "Sideloading is turned off on this PC, and fort.uwp cannot install without it." "Warning"
    Write-Status "Turning it on lets any package signed by a certificate you trust install" "Info"
    Write-Status "outside the Store, and it stays on afterwards until you turn it off in" "Info"
    Write-Status "Settings > Privacy & security > For developers." "Info"

    $enableSideloading = Read-Host "Turn sideloading on? (y/N)"
    if ($enableSideloading -ne "y" -and $enableSideloading -ne "Y") {
        Write-Status "Left sideloading turned off. Nothing was changed." "Error"
        Read-Host "Press Enter to exit"
        exit 1
    }

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

Write-Section "Dependencies"

$osArch = Get-OSArchitecture
Write-Status "Windows architecture: $osArch"

$runnableArchitectures = switch ($osArch) {
    "x86"   { @("x86") }
    "x64"   { @("x86", "x64") }
    "arm"   { @("arm") }
    "arm64" {
        if ($osVersion -ge [Version]"10.0.22000") { @("x86", "arm", "arm64", "x64") }
        else { @("x86", "arm", "arm64") }
    }
}

$dependencyPaths = @()
$dependencyRoot = Join-Path $PSScriptRoot "Dependencies"
if (Test-Path $dependencyRoot) {
    $dependencyFolders = @($dependencyRoot) + @($runnableArchitectures | ForEach-Object { Join-Path $dependencyRoot $_ })
    foreach ($folder in $dependencyFolders) {
        if (-not (Test-Path $folder)) { continue }
        $dependencyPaths += @(Get-ChildItem -Path $folder -File |
                              Where-Object { $_.Extension -in ".appx", ".msix" } |
                              ForEach-Object { $_.FullName })
    }
}

if ($dependencyPaths.Count -gt 0) {
    Write-Status "Found $($dependencyPaths.Count) bundled framework package(s); any that are missing will be installed with the app" "Success"
    foreach ($path in $dependencyPaths) {
        Write-Status (Split-Path $path -Leaf)
    }
} else {
    Write-Status "No bundled dependencies found next to this script (did you extract the whole zip?)" "Warning"
    Write-Status "The install will fail if a framework the app needs (WinUI 2.5, .NET Native, VCLibs) is missing" "Warning"
}

Write-Section "Package"

$packageCandidates = @(Get-ChildItem -Path $PSScriptRoot -File |
                       Where-Object { $_.Extension -in ".msix", ".appx" })

if ($packageCandidates.Count -eq 0) {
    Write-Status "seems like the APPX/MSIX package is missing, (did you extract the zip right?)" "Error"
    Read-Host "Press Enter to exit"
    exit 1
}

if ($packageCandidates.Count -gt 1) {
    Write-Status "There are $($packageCandidates.Count) packages next to this script:" "Error"
    foreach ($candidate in $packageCandidates) {
        Write-Status "  $($candidate.Name)" "Info"
    }
    Write-Status "Refusing to guess which one you meant. Extract the release zip into a" "Warning"
    Write-Status "folder of its own and run the installer from there." "Warning"
    Read-Host "Press Enter to exit"
    exit 1
}

$msixFile = $packageCandidates[0]
Write-Status "Package: $($msixFile.Name)" "Success"

$identity = Get-PackageIdentity $msixFile.FullName
if (-not $identity) {
    Write-Status "Could not read the package identity out of $($msixFile.Name)." "Error"
    Write-Status "Without it there is no way to tell which certificate should be trusted." "Error"
    Read-Host "Press Enter to exit"
    exit 1
}

if ($identity.Name -ne $ExpectedPackageName) {
    Write-Status "That package calls itself '$($identity.Name)', not '$ExpectedPackageName'." "Error"
    Write-Status "This installer only installs fort.uwp." "Error"
    Read-Host "Press Enter to exit"
    exit 1
}

$packagePublisher = Get-CanonicalDn $identity.Publisher
Write-Status "Publisher: $packagePublisher" "Success"

Write-Section "Signing Certificate"

$certCandidates = @(Get-ChildItem -Path $PSScriptRoot -Filter "*.cer" -File)

if ($certCandidates.Count -gt 1) {
    Write-Status "There are $($certCandidates.Count) .cer files next to this script:" "Error"
    foreach ($candidate in $certCandidates) {
        Write-Status "  $($candidate.Name)" "Info"
    }
    Write-Status "Refusing to guess which one to trust on this PC." "Warning"
    Read-Host "Press Enter to exit"
    exit 1
}

if ($certCandidates.Count -eq 0) {
    Write-Status "No certificate file found. The package may be unsigned." "Warning"
} else {
    $certFile = $certCandidates[0]

    $cert = $null
    try {
        $cert = Get-PfxCertificate -FilePath $certFile.FullName
    } catch {
        Write-Status "Could not read $($certFile.Name): $($_.Exception.Message)" "Error"
        Read-Host "Press Enter to exit"
        exit 1
    }

    $certSubject = Get-CanonicalDn $cert.Subject
    Write-Status "Subject:    $certSubject"
    Write-Status "Thumbprint: $($cert.Thumbprint)"

    if ($certSubject -ne $packagePublisher) {
        Write-Status "That certificate did not sign this package - its subject is" "Error"
        Write-Status "'$certSubject' but the package publisher is '$packagePublisher'." "Error"
        Write-Status "Installing it would trust a certificate unrelated to what you are installing." "Error"
        Read-Host "Press Enter to exit"
        exit 1
    }

    $knownPublisher = $false
    foreach ($publisher in $KnownPublishers) {
        if ((Get-CanonicalDn $publisher) -eq $certSubject) { $knownPublisher = $true }
    }

    if (-not $knownPublisher) {
        Write-Status "'$certSubject' is not a publisher this installer ships knowing about:" "Warning"
        foreach ($publisher in $KnownPublishers) {
            Write-Status "  $publisher" "Info"
        }
        Write-Status "Trusting it lets anything signed by it install on this PC. Only continue if" "Warning"
        Write-Status "you recognise the thumbprint above as one of fort.ind's." "Warning"

        $trustAnyway = Read-Host "Trust this certificate on this PC? (y/N)"
        if ($trustAnyway -ne "y" -and $trustAnyway -ne "Y") {
            Write-Status "Certificate not installed, so nothing was changed." "Error"
            Read-Host "Press Enter to exit"
            exit 1
        }
    }

    $existingCert = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople |
                    Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

    if ($existingCert) {
        Write-Status "Certificate is already installed" "Success"
    } else {
        try {
            Import-Certificate -FilePath $certFile.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            Write-Status "Certificate installed successfully" "Success"
        } catch {
            Write-Status "Failed to install certificate: $($_.Exception.Message)" "Error"
            $continue = Read-Host "Continue without the app cert? (y/N)"
            if ($continue -ne "y" -and $continue -ne "Y") {
                exit 1
            }
        }
    }
}

Write-Section "Installing Fort.uwp"
Write-Status "Installing Fort.uwp..."

$existingApp = Get-AppxPackage -Name $identity.Name -ErrorAction SilentlyContinue |
               Where-Object { $_.Publisher -eq $identity.Publisher }

if ($existingApp) {
    Write-Status "Upgrading over version $($existingApp.Version) - your settings and favourites are being moved over :)" "Info"
}

$installArgs = @{
    Path = $msixFile.FullName
    ForceApplicationShutdown = $true
    ErrorAction = "Stop"
}
if ($dependencyPaths.Count -gt 0) {
    $installArgs.DependencyPath = [string[]]$dependencyPaths
}

try {
    $installed = $false
    try {
        Add-AppxPackage @installArgs
        $installed = $true
    } catch {
        if (-not $existingApp) { throw }

        Write-Status "Retrying as a same-or-lower version upgrade..." "Warning"
        Add-AppxPackage @installArgs -ForceUpdateFromAnyVersion
        $installed = $true
    }

    if (-not $installed) { throw "The package could not be installed. :(" }

    Write-Status "Fort.uwp installed successfully!" "Success"
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
        Write-Status "As a last resort you can uninstall fort.uwp from Settings > Apps" "Info"
        Write-Status "and run this installer again - that WILL erase your app settings." "Warning"
    }
}

Write-Host ""
Read-Host "Press Enter to exit"
