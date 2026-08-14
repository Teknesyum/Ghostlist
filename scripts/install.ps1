param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\Ghostlist"),
    [switch]$NoLaunch,
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$releaseUrl = "https://github.com/Teknesyum/Ghostlist/releases/latest/download/Ghostlist-win-x64.zip"
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("Ghostlist-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempDirectory "Ghostlist-win-x64.zip"
$extractPath = Join-Path $tempDirectory "extracted"

try {
    Write-Host "Downloading the latest Ghostlist release..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $tempDirectory, $extractPath | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri $releaseUrl -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force

    Get-Process -Name "Ghostlist" -ErrorAction SilentlyContinue | Stop-Process -Force
    New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
    Copy-Item -Path (Join-Path $extractPath "*") -Destination $InstallDirectory -Recurse -Force

    $executable = Join-Path $InstallDirectory "Ghostlist.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The release archive does not contain Ghostlist.exe."
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
    if (-not $NoLaunch) {
        Start-Process -FilePath $executable -WorkingDirectory $InstallDirectory
    }
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
