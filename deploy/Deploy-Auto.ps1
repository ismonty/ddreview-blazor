<#
 Deploys staging automatically when the branch on GitHub has a new commit. Meant to be started every few minutes by a scheduled
 task (see Register-AutoDeploy.ps1 and docs/DEPLOY-STAGING.md). Windows PowerShell 5.1 or newer, Administrator.

 Each run: git fetch; if origin/<Branch> is a commit this server has not tried yet, switch the checkout to <Branch>, fast-forward it,
 and run Deploy-Staging.ps1 (tests first: failing tests deploy nothing and the old site keeps running). A commit that failed is not
 retried until a newer commit arrives. Output goes to auto-deploy.log next to the backups.
#>
param(
    [string]$Branch    = 'main',
    [string]$BackupDir = 'C:\inetpub\backups\blazor-staging.ddreview.com',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$log   = Join-Path $BackupDir 'auto-deploy.log'
$lock  = Join-Path $BackupDir 'auto-deploy.lock'
$tried = Join-Path $BackupDir 'auto-last-attempt.txt'
$live  = Join-Path $BackupDir 'last-deployed.txt'

function Log($m) { ('{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) | Add-Content -Path $log }
# git writes progress to stderr; keep that from being treated as a failure and check the exit code instead.
function Git { $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; $o = & git.exe @args 2>&1; $c = $LASTEXITCODE; $ErrorActionPreference = $old
               if ($c -ne 0) { throw ("git $($args -join ' ') failed: " + ($o | Out-String)) }; ($o | Out-String).Trim() }

if (Test-Path $lock) {
    if (((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 45) { exit 0 }   # a deploy is still running
    Log 'Removing a stale lock file.'
}
Set-Content -Path $lock -Value $PID
try {
    Git fetch origin $Branch --quiet | Out-Null
    $remote = Git rev-parse "origin/$Branch"
    $deployed = if (Test-Path $live) { (Get-Content $live -TotalCount 1).Trim() } else { '' }
    $attempted = if (Test-Path $tried) { (Get-Content $tried -TotalCount 1).Trim() } else { '' }
    if ($remote -eq $deployed -or $remote -eq $attempted) { exit 0 }                      # nothing new

    Log "New commit on ${Branch}: $remote ($(Git log -1 --format=%s $remote)). Deploying."
    Set-Content -Path $tried -Value $remote -Encoding ASCII
    if ((Git status --porcelain)) { throw 'The checkout has local changes; refusing to switch branches. Clean it up (git status).' }
    Git checkout $Branch | Out-Null
    Git merge --ff-only "origin/$Branch" | Out-Null

    $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Deploy-Staging.ps1'), '-SkipPull')
    if ($SkipTests) { $args2 += '-SkipTests' }
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & powershell.exe @args2 2>&1 | Out-File -FilePath $log -Append -Encoding UTF8
    $code = $LASTEXITCODE; $ErrorActionPreference = $old
    if ($code -ne 0) { throw "Deploy-Staging.ps1 failed (exit $code). Nothing newer will be tried until another commit arrives; see the log above." }
    Log "Deployed $remote."
}
catch { Log ('FAILED: ' + $_.Exception.Message); exit 1 }
finally { Remove-Item $lock -Force -ErrorAction SilentlyContinue }
