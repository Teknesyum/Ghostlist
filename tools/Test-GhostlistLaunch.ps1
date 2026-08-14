param([string]$ShortcutName = "Ghostlist.lnk")

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktop $ShortcutName
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)

$result = [ordered]@{
    Shortcut = $shortcutPath
    ShortcutExists = Test-Path -LiteralPath $shortcutPath
    Target = $shortcut.TargetPath
    TargetExists = Test-Path -LiteralPath $shortcut.TargetPath
    WorkingDirectory = $shortcut.WorkingDirectory
    DirectLaunchAlive = $false
    ShortcutLaunchAlive = $false
    ExitCode = $null
}

if ($result.TargetExists) {
    $process = Start-Process -FilePath $shortcut.TargetPath -WorkingDirectory $shortcut.WorkingDirectory -PassThru
    Start-Sleep -Seconds 4
    $process.Refresh()
    $result.DirectLaunchAlive = -not $process.HasExited
    if ($process.HasExited) {
        $result.ExitCode = $process.ExitCode
    }
    else {
        Stop-Process -Id $process.Id
    }
}

if ($result.ShortcutExists -and $result.TargetExists) {
    Start-Process -FilePath $shortcutPath
    Start-Sleep -Seconds 4
    $matching = Get-Process -Name "Ghostlist" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $shortcut.TargetPath }
    $result.ShortcutLaunchAlive = $null -ne $matching
    $matching | Stop-Process
}

[pscustomobject]$result

Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = (Get-Date).AddMinutes(-10) } -ErrorAction SilentlyContinue |
    Where-Object {
        $_.ProviderName -in @(".NET Runtime", "Application Error", "Windows Error Reporting") -and
        $_.Message -match "Ghostlist"
    } |
    Select-Object -First 5 TimeCreated, ProviderName, Id, Message |
    Format-List
