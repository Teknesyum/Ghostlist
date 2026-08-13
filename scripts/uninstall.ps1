$ErrorActionPreference = "Stop"
$installDirectory = Split-Path -Parent $PSCommandPath
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktop "ProgramFixer.lnk"

Get-Process -Name "ProgramFixer" -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$cleanupScript = Join-Path ([System.IO.Path]::GetTempPath()) ("ProgramFixer-cleanup-" + [Guid]::NewGuid().ToString("N") + ".ps1")
$escapedDirectory = $installDirectory.Replace("'", "''")
@"
Start-Sleep -Seconds 1
Remove-Item -LiteralPath '$escapedDirectory' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$PSCommandPath -Force -ErrorAction SilentlyContinue
"@ | Set-Content -LiteralPath $cleanupScript -Encoding UTF8

Write-Host "ProgramFixer was uninstalled. Registry backups were preserved." -ForegroundColor Green
Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $cleanupScript)

