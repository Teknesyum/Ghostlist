param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\ProgramFixer"),
    [switch]$NoLaunch,
    [switch]$NoShortcut
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$releaseUrl = "https://github.com/Teknesyum/ProgramFixer/releases/latest/download/ProgramFixer-win-x64.zip"
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("ProgramFixer-" + [Guid]::NewGuid().ToString("N"))
$archivePath = Join-Path $tempDirectory "ProgramFixer-win-x64.zip"
$extractPath = Join-Path $tempDirectory "extracted"

try {
    Write-Host "Downloading the latest ProgramFixer release..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $tempDirectory, $extractPath | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri $releaseUrl -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force

    Get-Process -Name "ProgramFixer" -ErrorAction SilentlyContinue | Stop-Process -Force
    New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
    Copy-Item -Path (Join-Path $extractPath "*") -Destination $InstallDirectory -Recurse -Force

    $executable = Join-Path $InstallDirectory "ProgramFixer.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "The release archive does not contain ProgramFixer.exe."
    }

    if (-not $NoShortcut) {
        $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
        $shortcutPath = Join-Path $desktop "ProgramFixer.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $InstallDirectory
        $shortcut.IconLocation = "$executable,0"
        $shortcut.Description = "ProgramFixer - safely fix orphaned Windows uninstall entries"
        $shortcut.Save()
    }

    Write-Host "ProgramFixer was installed successfully." -ForegroundColor Green
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
