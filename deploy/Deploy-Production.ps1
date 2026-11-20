<#
 Publishes to PRODUCTION (https://blazor.ddreview.com). Run by hand, as Administrator, never by a schedule:

   cd C:\src\ddreview-blazor
   .\deploy\Deploy-Production.ps1

 Safety rules (all checked before anything is touched):
   - the checkout is on main, has no local changes and equals origin/main;
   - that same commit is what the staging site is running (promote what you tested; -Force overrides);
   - production's appsettings.Production.json already exists;
   - you type DEPLOY after seeing the commit and what changed since the last production deploy.
 Then it runs Deploy-Staging.ps1 (tests, publish, backup zip, offline page, copy, check) against the production folder.
#>
param(
    [string]$SiteDir   = 'C:\inetpub\blazor.ddreview.com',
    [string]$BackupDir = 'C:\inetpub\backups\blazor.ddreview.com',
    [string]$Url       = 'https://blazor.ddreview.com/login',
    [string]$StagingMarker = 'C:\inetpub\backups\blazor-staging.ddreview.com\last-deployed.txt',
    [switch]$Force,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
function Git { $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; $o = & git.exe @args 2>&1; $c = $LASTEXITCODE; $ErrorActionPreference = $old
               if ($c -ne 0) { throw ("git $($args -join ' ') failed: " + ($o | Out-String)) }; ($o | Out-String).Trim() }

if (-not (Test-Path $SiteDir)) { throw "Production folder $SiteDir does not exist. Do the one-time setup first (docs/DEPLOY-PRODUCTION.md)." }
if (-not (Test-Path (Join-Path $SiteDir 'appsettings.Production.json'))) { throw "No appsettings.Production.json in $SiteDir. Production will not start without it (docs/DEPLOY-PRODUCTION.md, step 4)." }

Git fetch origin main --quiet | Out-Null
if ((Git rev-parse --abbrev-ref HEAD) -ne 'main') { throw 'The checkout is not on main. Run: git checkout main' }
if ((Git status --porcelain)) { throw 'The checkout has local changes. Clean it up first (git status).' }
Git merge --ff-only origin/main | Out-Null
$head = Git rev-parse HEAD

$staged = if (Test-Path $StagingMarker) { (Get-Content $StagingMarker -TotalCount 1).Trim() } else { '' }
if ($staged -ne $head -and -not $Force) {
    throw "Staging is not running this commit (staging: '$staged', this checkout: $head). Let the staging deploy finish and test it, or use -Force."
}
$prodMarker = Join-Path $BackupDir 'last-deployed.txt'
$prev = if (Test-Path $prodMarker) { (Get-Content $prodMarker -TotalCount 1).Trim() } else { '' }

Write-Host "`nAbout to deploy to PRODUCTION: $Url" -ForegroundColor Yellow
Write-Host ('Commit: ' + (Git log -1 --format='%h %s' HEAD))
if ($prev) { Write-Host "Changes since the last production deploy ($($prev.Substring(0,7))):"; Git log --oneline "$prev..HEAD" | Write-Host }
else       { Write-Host 'This is the first production deploy from this server.' }
if ((Read-Host "`nType DEPLOY to continue") -cne 'DEPLOY') { Write-Host 'Cancelled.'; exit 1 }

$a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Deploy-Staging.ps1'),
       '-SkipPull', '-SiteDir', $SiteDir, '-BackupDir', $BackupDir, '-Url', $Url)
if ($SkipTests) { $a += '-SkipTests' }
& powershell.exe @a
if ($LASTEXITCODE -ne 0) { throw "The deploy failed (exit $LASTEXITCODE). The backup zip in $BackupDir restores the previous version." }
Write-Host "`nProduction is on $($head.Substring(0,7))." -ForegroundColor Green
