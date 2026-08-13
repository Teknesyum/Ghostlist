param(
    [Parameter(Mandatory = $true)][string]$TargetPath,
    [string]$ShortcutName = "ProgramFixer.lnk"
)

$target = (Resolve-Path -LiteralPath $TargetPath).Path
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
$shortcutPath = Join-Path $desktop $ShortcutName
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = Split-Path $target
$shortcut.IconLocation = "$target,0"
$shortcut.Description = "ProgramFixer - Teknesyum"
$shortcut.Save()

$saved = $shell.CreateShortcut($shortcutPath)
[pscustomobject]@{
    Shortcut = $shortcutPath
    Target = $saved.TargetPath
    Icon = $saved.IconLocation
    Exists = Test-Path -LiteralPath $shortcutPath
}
