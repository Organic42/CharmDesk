# MSIX packaging

Builds CharmDesk as an MSIX package for local sideload testing and, eventually, Microsoft Store
submission. Requires the Windows 10/11 SDK (for `makeappx.exe` and `signtool.exe`).

## Build

```powershell
cd packaging
.\build-msix.ps1
```

This publishes CharmDesk (framework-dependent, matching the app's default), assembles it with
`Package.appxmanifest` and `Assets/`, packs it into `_out/CharmDesk.msix`, and signs it with a
local self-signed test certificate (created on first run, reused after that).

## Installing it locally

The build script signs with a certificate Windows doesn't trust by default - that's expected for
local testing and isn't a packaging problem. To actually install the built package, the signing
certificate needs to be added to the machine's Trusted Root store, which requires an elevated
PowerShell prompt:

```powershell
Import-Certificate -FilePath ".\CharmDesk-test.pfx" -CertStoreLocation Cert:\LocalMachine\Root
Add-AppxPackage -Path ".\_out\CharmDesk.msix"
```

(`Import-Certificate` doesn't take a PFX password directly - if prompted, use
`Import-PfxCertificate -FilePath .\CharmDesk-test.pfx -CertStoreLocation Cert:\LocalMachine\Root
-Password (ConvertTo-SecureString "charmdesk-local-test" -AsPlainText -Force)` instead.)

To uninstall: `Get-AppxPackage Organic42.CharmDesk | Remove-AppxPackage`

## Before submitting to the Microsoft Store

1. Reserve the app name in [Partner Center](https://partner.microsoft.com/dashboard) - this
   assigns the real `Identity/Name` and `Identity/Publisher` values.
2. Update those two attributes in `Package.appxmanifest` (see the comment at the top of that
   file). The placeholder values here (`Organic42.CharmDesk` / `CN=Organic42`) are for local
   testing only.
3. You don't need to sign the submission yourself - Partner Center re-signs on certification.
   Build with `.\build-msix.ps1` and skip (or ignore) the signing step, or use `dotnet publish`
   with an `.msixupload` target if you set up the full Windows Application Packaging Project in
   Visual Studio instead of this script.
4. Store-listing assets (screenshots, feature graphic, privacy policy URL) are separate from the
   package itself and are uploaded directly in Partner Center.
