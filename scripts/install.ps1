param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\Ghostlist"),
    [switch]$NoLaunch,
    [switch]$NoShortcut,
    [switch]$SkipHashCheck
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$releaseUrl = "https://github.com/Teknesyum/Ghostlist/releases/latest/download/Ghostlist-win-x64.zip"
$hashUrl = "$releaseUrl.sha256"
$uninstallUrl = "https://raw.githubusercontent.com/Teknesyum/Ghostlist/main/scripts/uninstall.ps1"
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("Ghostlist-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempDirectory "Ghostlist-win-x64.zip"
$hashPath = "$archivePath.sha256"
$extractPath = Join-Path $tempDirectory "extracted"
$exitCode = 0

function Write-Failure {
    param([string]$Message, [int]$Code)
    Write-Host $Message -ForegroundColor Red
    $script:exitCode = $Code
}

try {
    New-Item -ItemType Directory -Force -Path $tempDirectory, $extractPath | Out-Null

    Write-Host "Downloading the latest Ghostlist release..." -ForegroundColor Cyan
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $releaseUrl -OutFile $archivePath
    }
    catch {
        throw [System.Management.Automation.RuntimeException]::new("DOWNLOAD|Could not download the release archive from $releaseUrl. $($_.Exception.Message)")
    }

    if ($SkipHashCheck) {
        Write-Host "Skipping integrity verification because -SkipHashCheck was supplied." -ForegroundColor Yellow
    }
    else {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $hashUrl -OutFile $hashPath
        }
        catch {
            throw [System.Management.Automation.RuntimeException]::new("DOWNLOAD|Could not download the checksum file from $hashUrl. $($_.Exception.Message)")
        }

        $expected = ((Get-Content -LiteralPath $hashPath -Raw) -split '\s+' | Where-Object { $_ }) | Select-Object -First 1
        if (-not $expected) {
            throw [System.Management.Automation.RuntimeException]::new("HASH|The checksum file $hashUrl is empty or malformed.")
        }
        $expected = $expected.ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
            throw [System.Management.Automation.RuntimeException]::new("HASH|Integrity check failed. Expected SHA256 $expected but the downloaded file is $actual. The archive was deleted and nothing was installed.")
        }
        Write-Host "Integrity verified (SHA256 $actual)." -ForegroundColor Green
    }

    try {
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
    }
    catch {
        throw [System.Management.Automation.RuntimeException]::new("ARCHIVE|The downloaded archive could not be extracted; it is corrupt or incomplete. $($_.Exception.Message)")
    }

    if (-not (Test-Path -LiteralPath (Join-Path $extractPath "Ghostlist.exe"))) {
        throw [System.Management.Automation.RuntimeException]::new("ARCHIVE|The release archive does not contain Ghostlist.exe.")
    }

    Get-Process -Name "Ghostlist" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    try {
        New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
        Copy-Item -Path (Join-Path $extractPath "*") -Destination $InstallDirectory -Recurse -Force
    }
    catch {
        throw [System.Management.Automation.RuntimeException]::new("LOCKED|Could not write to $InstallDirectory. Close Ghostlist and any window browsing that folder, then run the installer again. $($_.Exception.Message)")
    }

    $executable = Join-Path $InstallDirectory "Ghostlist.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw [System.Management.Automation.RuntimeException]::new("LOCKED|Ghostlist.exe was not written to $InstallDirectory. The installation is incomplete.")
    }

    $uninstallScript = Join-Path $InstallDirectory "uninstall.ps1"
    if (-not (Test-Path -LiteralPath $uninstallScript)) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $uninstallUrl -OutFile $uninstallScript
        }
        catch {
            Write-Host "Warning: the uninstall script could not be downloaded. Delete $InstallDirectory by hand to remove Ghostlist." -ForegroundColor Yellow
        }
    }

    if (-not $NoShortcut) {
        $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
        $shortcutPath = Join-Path $desktop "Ghostlist.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $InstallDirectory
        $shortcut.IconLocation = "$executable,0"
        $shortcut.Description = "Ghostlist - safely fix orphaned Windows uninstall entries"
        $shortcut.Save()
    }

    Write-Host "Ghostlist was installed successfully." -ForegroundColor Green
    Write-Host "Location: $InstallDirectory"
    Write-Host "Command line: $(Join-Path $InstallDirectory 'cli\ghostlist.exe')"
    if (-not $NoLaunch) {
        Start-Process -FilePath $executable -WorkingDirectory $InstallDirectory
    }
}
catch {
    $parts = $_.Exception.Message -split '\|', 2
    if ($parts.Count -eq 2) {
        $tag = $parts[0]
        $message = $parts[1]
    }
    else {
        $tag = "OTHER"
        $message = $_.Exception.Message
    }

    switch ($tag) {
        "DOWNLOAD" { Write-Failure $message 2 }
        "HASH"     { Write-Failure $message 3 }
        "ARCHIVE"  { Write-Failure $message 4 }
        "LOCKED"   { Write-Failure $message 5 }
        default    { Write-Failure "Installation failed: $message" 1 }
    }
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
