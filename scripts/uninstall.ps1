param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\Ghostlist"
if (-not (Test-Path -LiteralPath (Join-Path $installDirectory "Ghostlist.exe"))) {
    Write-Host "Ghostlist does not look installed at $installDirectory. Nothing was removed." -ForegroundColor Yellow
    exit 7
}
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktop "Ghostlist.lnk"
$backupDirectory = Join-Path $env:LOCALAPPDATA "Ghostlist\Backups"

$running = @(Get-Process -Name "Ghostlist" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    if (-not $Force) {
        Write-Host "Ghostlist is still running. Close it and run this script again, or re-run with -Force to close it automatically." -ForegroundColor Yellow
        exit 6
    }

    Write-Host "Closing Ghostlist because -Force was supplied..." -ForegroundColor Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$cleanupScript = Join-Path ([System.IO.Path]::GetTempPath()) ("Ghostlist-cleanup-" + [Guid]::NewGuid().ToString("N") + ".ps1")
$escapedDirectory = $installDirectory.Replace("'", "''")
@"
Start-Sleep -Seconds 1
Remove-Item -LiteralPath '$escapedDirectory' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$PSCommandPath -Force -ErrorAction SilentlyContinue
"@ | Set-Content -LiteralPath $cleanupScript -Encoding UTF8

Write-Host "Ghostlist was uninstalled." -ForegroundColor Green
Write-Host "Your backups were NOT deleted. They stay in $backupDirectory so anything Ghostlist changed can still be restored. Delete that folder by hand if you no longer want them."
Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $cleanupScript)
exit 0
