<#
 Builds this checkout and publishes it to the IIS site folder (Windows PowerShell 5.1 or newer, run as Administrator).

   cd C:\src\ddreview-blazor
   .\deploy\Deploy-Staging.ps1

 It pulls the branch you have checked out, runs the tests, publishes, backs up the live folder, takes the site offline
 (app_offline.htm), copies the new files over WITHOUT touching appsettings.Production.json, the keys and the logs, and
 brings the site back. Re-run it for every update.
#>
param(
    [string]$SiteDir  = 'C:\inetpub\blazor.ddreview.com',
    [string]$BackupDir = 'C:\inetpub\backups\blazor.ddreview.com',
    [string]$Url      = 'https://blazor.ddreview.com/login',
    [switch]$SkipPull,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Step($m) { Write-Host ("`n== " + $m) -ForegroundColor Cyan }

Step 'Checking tools'
$sdk = (& dotnet --version) 2>$null
if (-not $sdk -or -not $sdk.StartsWith('10.')) { throw "The .NET 10 SDK is required on this server (found: '$sdk'). Install it from https://dotnet.microsoft.com/download/dotnet/10.0" }
if (-not (Test-Path $SiteDir)) { throw "Site folder $SiteDir does not exist. Create it and the IIS site first (docs/DEPLOY-STAGING.md)." }
if (-not (Test-Path (Join-Path $SiteDir 'appsettings.Production.json'))) { Write-Warning "No appsettings.Production.json in $SiteDir yet - the site will not be able to log anyone in until you create it." }

if (-not $SkipPull) {
    Step 'Pulling the latest code'
    git pull --ff-only
    if ($LASTEXITCODE -ne 0) { throw 'git pull failed' }
}
Write-Host ('Deploying commit: ' + (git log -1 --format='%h %s'))

if (-not $SkipTests) {
    Step 'Running the tests'
    dotnet test tests\DDReview.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed - nothing was deployed.' }
}

Step 'Publishing'
$out = Join-Path $env:TEMP ('ddreview-publish-' + [guid]::NewGuid().ToString('N'))
dotnet publish src\DDReview.Web\DDReview.Web.csproj -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw 'Publish failed - nothing was deployed.' }

Step 'Backing up the live folder'
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$zip = Join-Path $BackupDir ('site-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Get-ChildItem $SiteDir -Force | Where-Object { $_.Name -ne 'logs' }) {
    [System.IO.Compression.ZipFile]::CreateFromDirectory($SiteDir, $zip)
    Write-Host "Backup: $zip"
}
Get-ChildItem $BackupDir -Filter 'site-*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -Skip 8 | Remove-Item -Force

Step 'Taking the site offline and copying files'
$offline = Join-Path $SiteDir 'app_offline.htm'
'<html><body style="font-family:sans-serif"><h2>DD Review is being updated.</h2><p>Back in a minute.</p></body></html>' | Set-Content -Path $offline -Encoding UTF8
Start-Sleep -Seconds 3
# /MIR makes the folder match the publish output; the excluded files and folders are the server's own and are left alone.
robocopy $out $SiteDir /MIR /NFL /NDL /NJH /NJS /NP /XF appsettings.Production.json appsettings.Staging.json app_offline.htm /XD logs keys
if ($LASTEXITCODE -ge 8) { Remove-Item $offline -Force -ErrorAction SilentlyContinue; throw "robocopy failed (exit $LASTEXITCODE). Restore from $zip if needed." }
Remove-Item $offline -Force
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

Step 'Checking the site answers'
try {
    $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 90
    Write-Host ("OK: $Url returned HTTP " + $r.StatusCode) -ForegroundColor Green
} catch {
    Write-Warning ("Could not fetch $Url : " + $_.Exception.Message)
    Write-Warning 'If DNS or the certificate is not ready yet this is expected; try http://localhost:<port> through an IIS binding, or see docs/DEPLOY-STAGING.md (Troubleshooting).'
}
Write-Host "`nDone." -ForegroundColor Green
