<#
.SYNOPSIS
    Builds CharmDesk.msix for local sideload testing (framework-dependent, matching the app's
    default publish shape). Also signs it with a local self-signed test certificate so it can
    actually be installed without a Store/Partner-Center-issued certificate.

.NOTES
    This is for LOCAL TESTING ONLY. A real Store submission uses an .msixupload produced the
    same way but does NOT need to be signed here - Partner Center signs it during certification.
    See the notice at the top of Package.appxmanifest before changing Identity/Publisher.
#>

$ErrorActionPreference = "Stop"

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..")
$srcProj    = Join-Path $repoRoot "src\CharmDesk\CharmDesk.csproj"
$stagingDir = Join-Path $PSScriptRoot "_staging"
$outDir     = Join-Path $PSScriptRoot "_out"
$msixPath   = Join-Path $outDir "CharmDesk.msix"
$sdkBin     = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64"
$makeappx   = Join-Path $sdkBin "makeappx.exe"
$signtool   = Join-Path $sdkBin "signtool.exe"
$certSubject = "CN=Organic42"
$pfxPath    = Join-Path $PSScriptRoot "CharmDesk-test.pfx"
$pfxPassword = "charmdesk-local-test"

Write-Host "== 1/5: Publishing CharmDesk (framework-dependent) ==" -ForegroundColor Cyan
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir | Out-Null
dotnet publish $srcProj -c Release -o $stagingDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "== 2/5: Assembling package layout ==" -ForegroundColor Cyan
Copy-Item (Join-Path $PSScriptRoot "Package.appxmanifest") (Join-Path $stagingDir "AppxManifest.xml") -Force
Copy-Item (Join-Path $PSScriptRoot "Assets") (Join-Path $stagingDir "Assets") -Recurse -Force
# PDBs aren't part of the shipped package.
Get-ChildItem $stagingDir -Filter "*.pdb" -Recurse | Remove-Item -Force

Write-Host "== 3/5: Packing MSIX ==" -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null
& $makeappx pack /d $stagingDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

Write-Host "== 4/5: Preparing local test certificate ==" -ForegroundColor Cyan
$existingCert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $existingCert) {
    Write-Host "Creating new self-signed cert ($certSubject)..."
    $existingCert = New-SelfSignedCertificate -Type Custom -Subject $certSubject `
        -KeyUsage DigitalSignature -FriendlyName "CharmDesk local test signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
$securePw = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $existingCert -FilePath $pfxPath -Password $securePw | Out-Null

# Trust it locally (CurrentUser store, no admin needed) so Add-AppxPackage will accept it.
$trustStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "CurrentUser")
$trustStore.Open("ReadWrite")
if (-not ($trustStore.Certificates | Where-Object { $_.Thumbprint -eq $existingCert.Thumbprint })) {
    $trustStore.Add($existingCert)
}
$trustStore.Close()

Write-Host "== 5/5: Signing package ==" -ForegroundColor Cyan
& $signtool sign /fd SHA256 /a /f $pfxPath /p $pfxPassword $msixPath
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }

Write-Host ""
Write-Host "Built and signed: $msixPath" -ForegroundColor Green
Write-Host "Install locally with:  Add-AppxPackage -Path `"$msixPath`"" -ForegroundColor Green
