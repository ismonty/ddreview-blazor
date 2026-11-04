<#
 Builds DDQuickBooksPoster.exe and zips it for the PCs that run QuickBooks (Windows PowerShell 5.1 or newer; needs the .NET 10 SDK on this
 computer, not QuickBooks).

   cd C:\EOD\ddreview-blazor
   .\deploy\Publish-Poster.ps1

 It runs the tests, publishes the 32-bit self-contained program (QuickBooks' request processor is a 32-bit COM server, so the poster is x86),
 leaves out the .pdb files, checks that the trusted-keys file is in the folder and lists its key ids (those public keys are what let the poster
 accept "Post to QuickBooks" links from the website), and writes  artifacts\DDQuickBooksPoster-<date>-<commit>.zip  (about 47 MB).

 It also builds a per-user installer, artifacts\DDQuickBooksPoster-<date>-<commit>.msi (WiX, installed as a local .NET tool; needs Windows). The website's
 Setup page offers that MSI for download: pass -InstallerFolder with the folder the site's PosterInstaller:Folder setting points to and the MSI is copied there too.

 On the PC that will use it: either run the MSI (installs for that Windows user only, no administrator rights, adds a Start menu shortcut and the
 website link), or right-click the zip > Properties > Unblock, unzip anywhere and run DDQuickBooksPoster.exe.
 Re-run this for every new build or whenever deploy\poster-trusted-keys.json changes.
#>
param(
    [string]$OutDir = '',
    [string]$InstallerFolder = '',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts' }

function Step($m) { Write-Host ("`n== " + $m) -ForegroundColor Cyan }

Step 'Checking tools'
$sdk = (& dotnet --version) 2>$null
if (-not $sdk -or -not $sdk.StartsWith('10.')) { throw "The .NET 10 SDK is required (found: '$sdk'). Install it from https://dotnet.microsoft.com/download/dotnet/10.0" }
$sha = (& git rev-parse --short HEAD) 2>$null
if (-not $sha) { $sha = 'nogit' }
$dirty = (& git status --porcelain) 2>$null
if ($dirty) { Write-Warning 'There are uncommitted changes: this build will not match any commit. The zip name says so.'; $sha = $sha + '-dirty' }

if (-not $SkipTests) {
    Step 'Running the tests'
    & dotnet test (Join-Path $repo 'tests/DDReview.Tests/DDReview.Tests.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'The tests failed; nothing was published. Fix them or re-run with -SkipTests if you really mean it.' }
}

Step 'Checking the trusted keys'
$keysSource = Join-Path $repo 'deploy/poster-trusted-keys.json'
$keyIds = @()
if (-not (Test-Path $keysSource)) { throw "$keysSource is missing." }
try {
    $json = (Get-Content $keysSource -Raw) -replace '(?m)^\s*//.*$', ''
    $keyIds = @((ConvertFrom-Json $json).keys | ForEach-Object { $_.kid })
} catch { throw "deploy/poster-trusted-keys.json is not valid JSON: $($_.Exception.Message)" }
if ($keyIds.Count -eq 0) {
    Write-Warning 'The trusted-keys file has no keys: this poster will refuse every link from the website (it still works when started by hand).'
} else {
    Write-Host ('Trusted key ids: ' + ($keyIds -join ', '))
}

Step 'Publishing the poster (win-x86, self-contained)'
$publishDir = Join-Path $OutDir 'poster'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
& dotnet publish (Join-Path $repo 'src/DDReview.QuickBooks.Win/DDReview.QuickBooks.Win.csproj') -c Release -r win-x86 --self-contained true -o $publishDir -p:SourceRevisionId=$sha --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Step 'Tidying and checking the folder'
Get-ChildItem $publishDir -Recurse -Filter '*.pdb' | Remove-Item -Force
foreach ($need in 'DDQuickBooksPoster.exe', 'DDQuickBooksPoster.keys.json') {
    if (-not (Test-Path (Join-Path $publishDir $need))) { throw "$need is not in the published folder." }
}

Step 'Zipping'
$stamp = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmm'), $sha
$zip = Join-Path $OutDir "DDQuickBooksPoster-$stamp.zip"
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -Force
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Step 'Building the installer (MSI)'
$msi = Join-Path $OutDir "DDQuickBooksPoster-$stamp.msi"
$msiError = $null
try {
    $verMatch = [regex]::Match((Get-Content (Join-Path $repo 'src/DDReview.QuickBooks.Win/DDReview.QuickBooks.Win.csproj') -Raw), '<Version>(\d+\.\d+\.\d+)</Version>')
    if (-not $verMatch.Success) { throw 'No <Version> in DDReview.QuickBooks.Win.csproj.' }
    $count = (& git rev-list --count HEAD) 2>$null
    if (-not $count) { $count = '0' }
    $msiVersion = $verMatch.Groups[1].Value + '.' + [math]::Min([int]$count, 65535)
    & dotnet tool restore --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed (the WiX tool could not be installed).' }
    & dotnet tool run wix -- build (Join-Path $repo 'installer/DDQuickBooksPoster.wxs') -d "Version=$msiVersion" -bindpath "publish=$publishDir" -arch x86 -pdbtype none -o $msi
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msi)) { throw 'wix build failed.' }
    if ($InstallerFolder) {
        New-Item -ItemType Directory -Path $InstallerFolder -Force | Out-Null
        Copy-Item $msi $InstallerFolder -Force
    }
} catch { $msiError = $_.Exception.Message }

Write-Host ''
Write-Host "Done: $zip ($mb MB)" -ForegroundColor Green
Write-Host 'On the target PC: right-click the zip > Properties > Unblock, unzip anywhere, run DDQuickBooksPoster.exe once and answer Yes to the links question.'
if ($keyIds.Count -gt 0) { Write-Host ('This build trusts links signed by: ' + ($keyIds -join ', ')) }
if ($msiError) { Write-Warning "The installer (MSI) was NOT built: $msiError  The zip above is fine."; exit 1 }
else {
    $msiMb = [math]::Round((Get-Item $msi).Length / 1MB, 1)
    Write-Host "Installer: $msi ($msiMb MB). Double-click it on a PC to install for that Windows user (no administrator rights)." -ForegroundColor Green
    if ($InstallerFolder) { Write-Host "Copied to $InstallerFolder for the website's Setup page." }
    else { Write-Host 'To offer it on the website, copy it into the folder named by the site setting PosterInstaller:Folder (or re-run with -InstallerFolder <that folder>).' }
}
