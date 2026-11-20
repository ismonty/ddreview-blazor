# Deploying DD Review (Blazor) to production: blazor.ddreview.com on IIS

Production is a second copy of the same site on the same server, built from the same checkout. It is deployed **by hand**, only with a commit that staging is already running
(`deploy\Deploy-Production.ps1`). Staging is the rehearsal: nothing reaches production that has not been through staging first.
The staging guide (`docs/DEPLOY-STAGING.md`) covers the server prerequisites (IIS, WebSocket Protocol, .NET 10 SDK and Hosting Bundle, Git, the checkout); this page only lists what is different.

## One-time setup (Administrator PowerShell)

1. **DNS and certificate** for `blazor.ddreview.com` (you handle these). Sign-in only works over HTTPS.
2. **Folder, app pool and site** (its own of each; nothing shared with staging, MOOTT or Bob):
   ```
   New-Item -ItemType Directory C:\inetpub\blazor.ddreview.com
   Import-Module WebAdministration
   New-WebAppPool ddreview-production
   Set-ItemProperty IIS:\AppPools\ddreview-production managedRuntimeVersion ''      # No Managed Code
   New-Website -Name blazor.ddreview.com -PhysicalPath C:\inetpub\blazor.ddreview.com -ApplicationPool ddreview-production -HostHeader blazor.ddreview.com -Port 80
   New-Item -ItemType Directory C:\inetpub\blazor.ddreview.com\keys
   icacls C:\inetpub\blazor.ddreview.com /grant "IIS AppPool\ddreview-production:(OI)(CI)M"
   ```
   Add the HTTPS binding (IIS Manager > Sites > blazor.ddreview.com > Bindings > Add > https, host name `blazor.ddreview.com`, tick *Require Server Name Indication*, pick the certificate).
3. **Which databases production uses.** Decide whether production reads the same central master as staging or a different one. Either way the SQL login for production needs its own user
   in the central master with `SELECT` on `tblWebUserSecurity` and `SELECT, INSERT, UPDATE, DELETE` on `tblWebUserGoogle` (script `sql\003`), and in each client database the grants from
   `dbo.usp_DDReviewGrantAccess` (use *Database updater* in the app, then `exec dbo.usp_DDReviewGrantAccess @UserName = N'<web login user>', @Execute = 1;`). Use a **different SQL password** from staging.
4. **Settings file with the secrets** (never in git): copy `deploy\appsettings.Production.example.json` to `C:\inetpub\blazor.ddreview.com\appsettings.Production.json` and set
   `Tenancy:CentralConnectionString`, leave `ClientConnectionStringOverride` as `null`, set `DataProtection:KeysPath` to `C:\\inetpub\\blazor.ddreview.com\\keys`
   and `AllowedHosts` to `blazor.ddreview.com`. Remember `\` is written `\\` in JSON.
5. **Google sign-in:** in the same Google Cloud OAuth client for DD Review add the authorized redirect URI `https://blazor.ddreview.com/signin-google` (or make a second client and put its ID and secret in
   production's settings file). Put the Client ID and Secret in `Authentication:Google`. Google keeps a consent screen in *Testing* mode limited to listed test users; to let customers use Google sign-in,
   publish the consent screen (APIs & Services > OAuth consent screen > Publish app). Each person links Google once from the Account page.
6. **Users:** nothing to copy: logins come from the central master. If production uses the same master as staging, everyone can already sign in.

## Deploying (and every later release)

1. Merge to `main`. Staging updates itself within about five minutes (or run `.\deploy\Deploy-Staging.ps1`). Open https://blazor-staging.ddreview.com and test what changed.
2. When you are satisfied:
   ```
   cd C:\src\ddreview-blazor
   .\deploy\Deploy-Production.ps1
   ```
   It refuses unless the checkout is on `main`, clean, equal to GitHub's `main`, and the commit is the one staging is running. It shows the commit and what changed since the last production deploy,
   and you type `DEPLOY`. Then it runs the tests, publishes, zips the live folder to `C:\inetpub\backups\blazor.ddreview.com` (last 8 kept), shows the "being updated" page, copies the files
   (never `appsettings.Production.json`, `keys` or `logs`), and checks the login page.
3. Smoke test: sign in, run Sales and Receipts for a known day, compare with turbo.ddreview.com.

Options: `-Force` (deploy a commit staging has not run; avoid), `-SkipTests`.

## Rolling back
Stop the site, unzip the newest good file from `C:\inetpub\backups\blazor.ddreview.com` over `C:\inetpub\blazor.ddreview.com` (leave `appsettings.Production.json` and `keys`), start the site:
```
Stop-Website blazor.ddreview.com
Expand-Archive -Force C:\inetpub\backups\blazor.ddreview.com\site-YYYYMMDD-HHMMSS.zip C:\inetpub\blazor.ddreview.com
Start-Website blazor.ddreview.com
```
The `last-deployed.txt` marker does not need restoring; the next deploy rewrites it. The databases are never changed by a deploy (only by scripts you run), so a rollback does not touch data.

## Cutting over from turbo.ddreview.com
The old site keeps running. Move customers over one at a time: send them the new address, leave turbo.ddreview.com up until they are comfortable. Nothing in the databases ties a customer to one site, so
both can run side by side. When the last customer has moved, point turbo.ddreview.com at the new site (add the host name binding and certificate to this site and update `AllowedHosts`) or retire it.
