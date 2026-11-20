<#
 Creates the scheduled task that runs Deploy-Auto.ps1 every few minutes. Run once, in an Administrator PowerShell:

   cd C:\src\ddreview-blazor
   .\deploy\Register-AutoDeploy.ps1

 It asks for the Windows account the task runs as: a local administrator that can run `git fetch` in this checkout without a prompt
 (see docs/DEPLOY-STAGING.md, "Updating staging automatically"). Remove it with: Unregister-ScheduledTask 'DD Review staging auto-deploy'
#>
param(
    [int]$Minutes = 5,
    [string]$TaskName = 'DD Review staging auto-deploy'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$script = Join-Path $PSScriptRoot 'Deploy-Auto.ps1'
$cred = Get-Credential -UserName "$env:USERDOMAIN\$env:USERNAME" -Message 'Windows account to run the auto-deploy as (a local administrator)'

$action   = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument ('-NoProfile -ExecutionPolicy Bypass -File "' + $script + '"') -WorkingDirectory $repo
$trigger  = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $Minutes) -RepetitionDuration (New-TimeSpan -Days 3650)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 40) -StartWhenAvailable
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -User $cred.UserName -Password $cred.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
Write-Host "Scheduled '$TaskName' every $Minutes minutes as $($cred.UserName). Log: C:\inetpub\backups\blazor-staging.ddreview.com\auto-deploy.log" -ForegroundColor Green
