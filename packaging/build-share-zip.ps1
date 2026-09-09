<#
.SYNOPSIS
    Builds a self-contained, no-install CharmDesk zip for handing to testers.

.DESCRIPTION
    Self-contained on purpose: testers just unzip and double-click, with no .NET runtime to
    install first. That costs ~155MB versus the ~3MB framework-dependent build, which is the
    right trade for casual testing but NOT what ships to the Store.

    Verifies the payload before zipping. Content files have been observed to intermittently go
    missing from publish output on this machine (security software appears to lock individual
    files mid-copy), and a charm silently shipping without its image is exactly the kind of
    thing a tester would hit and nobody would understand.
#>

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$srcProj  = Join-Path $repoRoot "src\CharmDesk\CharmDesk.csproj"
$charmSrc = Join-Path $repoRoot "charms"
$stageDir = Join-Path ([Environment]::GetFolderPath("Desktop")) "CharmDesk-share"
$zipPath  = Join-Path ([Environment]::GetFolderPath("Desktop")) "CharmDesk-v1.0-test.zip"

Write-Host "== Publishing self-contained build ==" -ForegroundColor Cyan
Remove-Item $stageDir -Recurse -Force -ErrorAction SilentlyContinue
# ReadyToRun precompiles to native code, so the first launch isn't waiting on the JIT for the
# whole WPF startup path. Costs some file size, buys a noticeably faster cold start on the
# modest laptops this build actually gets handed to. Not trimming: IL trimming is unsupported
# for WPF and silently breaks XAML/reflection paths (see the note in CharmDesk.csproj).
dotnet publish $srcProj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None -p:DebugSymbols=false -o $stageDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "== Verifying payload ==" -ForegroundColor Cyan

# Every file that exists under charms/ in the repo must exist in the output.
$missing = @()
Get-ChildItem $charmSrc -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($charmSrc.Length).TrimStart('\')
    $dest = Join-Path $stageDir "charms\$rel"
    if (-not (Test-Path $dest)) { $missing += "charms\$rel" }
}

if (-not (Test-Path (Join-Path $stageDir "CharmDesk.exe"))) { $missing += "CharmDesk.exe" }

if ($missing.Count -gt 0) {
    Write-Host "Missing from published output:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw "Payload incomplete - refusing to build a broken share zip. Re-run; this is usually transient."
}
Write-Host "All charm assets and the exe are present." -ForegroundColor Green

Copy-Item (Join-Path $PSScriptRoot "share-readme.txt") (Join-Path $stageDir "READ ME FIRST.txt") -Force

# Escape hatch back to the old layered-window rendering, without needing to set an environment
# variable by hand. Plain CharmDesk.exe uses the DWM path, which is the one that fixed the
# flickering on affected laptops.
Get-ChildItem $PSScriptRoot -Filter "Run - old rendering*.bat" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $stageDir $_.Name) -Force
}

Write-Host "== Zipping ==" -ForegroundColor Cyan
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal

$zip = Get-Item $zipPath
Write-Host ""
Write-Host "Built: $($zip.FullName)" -ForegroundColor Green
Write-Host "Size:  $([math]::Round($zip.Length/1MB,1)) MB" -ForegroundColor Green
