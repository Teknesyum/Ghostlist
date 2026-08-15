# winget manifest

These three files are the winget package manifest for `Teknesyum.Ghostlist`. They are kept
in this repository on purpose and are **not** submitted anywhere automatically. Publishing to
`microsoft/winget-pkgs` is a manual decision.

## Before submitting

1. `PackageVersion` in all three files must match `<Version>` in `Directory.Build.props`.
2. `InstallerUrl` must point at the `Ghostlist-win-x64.zip` asset of the matching release tag.
3. `InstallerSha256` must be the hash from that release's `Ghostlist-win-x64.zip.sha256`
   asset, uppercased. The placeholder of sixty-four zeros means "not filled in yet".

To fill the hash in for release `v2.0.0`:

```powershell
$version = '2.0.0'
$url = "https://github.com/Teknesyum/Ghostlist/releases/download/v$version/Ghostlist-win-x64.zip"
Invoke-WebRequest $url -OutFile "$env:TEMP\Ghostlist-win-x64.zip"
(Get-FileHash "$env:TEMP\Ghostlist-win-x64.zip" -Algorithm SHA256).Hash
```

Then validate and submit by hand:

```powershell
winget validate --manifest packaging\winget
winget install --manifest packaging\winget
```

`CI` checks that the manifest version tracks `Directory.Build.props`; it does not check the
hash, because the hash only exists once the release is published.
