# Deploying DD Review (Blazor) to staging: blazor.ddreview.com on IIS

The server builds the site itself from git. One script does a deploy and the same script does every update.

## One-time setup on the server (Administrator PowerShell unless noted)

1. **Windows features.** Server Manager > Add Roles and Features > Web Server (IIS), and under Application Development tick
   **WebSocket Protocol** (Blazor Server needs it). Or:
   `Install-WindowsFeature Web-Server, Web-WebSockets, Web-Mgmt-Console`
2. **.NET 10.** Install, from https://dotnet.microsoft.com/download/dotnet/10.0, the **SDK** (it builds the site) and the
   **ASP.NET Core Hosting Bundle** (it lets IIS run it). Install the Hosting Bundle after IIS, then run `iisreset`.
   Check: `dotnet --version` starts with `10.` (Windows Server 2016 or newer is the safe choice; look at Microsoft's .NET 10 supported-OS list for anything older).
3. **Git** (https://git-scm.com) and a clone of the private repo. Use a personal access token or a deploy key for ismonty/ddreview-blazor:
   ```
   git clone https://github.com/ismonty/ddreview-blazor.git C:\src\ddreview-blazor
   cd C:\src\ddreview-blazor
   git checkout slice-2-sales-review     # or main, once pull request #1 is merged
   ```
4. **Site folder and IIS site.**
   ```
   New-Item -ItemType Directory C:\inetpub\blazor.ddreview.com
   Import-Module WebAdministration
   New-WebAppPool blazor.ddreview.com
   Set-ItemProperty IIS:\AppPools\blazor.ddreview.com managedRuntimeVersion ''      # No Managed Code
   New-Website -Name blazor.ddreview.com -PhysicalPath C:\inetpub\blazor.ddreview.com -ApplicationPool blazor.ddreview.com -HostHeader blazor.ddreview.com -Port 80
   icacls C:\inetpub\blazor.ddreview.com /grant "IIS AppPool\blazor.ddreview.com:(OI)(CI)M"
   ```
   When DNS and your certificate are ready add the HTTPS binding: IIS Manager > Sites > blazor.ddreview.com > Bindings > Add > https,
   host name `blazor.ddreview.com`, tick *Require Server Name Indication*, pick the certificate. **Sign-in only works over HTTPS** (the login cookie is Secure-only).
5. **Settings file with the secrets** (never in git). Copy `deploy\appsettings.Production.example.json` to
   `C:\inetpub\blazor.ddreview.com\appsettings.Production.json` and fill in:
   - `Tenancy:CentralConnectionString`: the central master database (DDReview-CentralMaster) and its SQL login.
   - `Tenancy:ClientConnectionStringOverride`: leave `null`. If set, every login is sent to that one database.
   - `Tenancy:TrustClientServerCertificate`: `true` if client SQL servers use self-signed certificates, `false` once they have real ones.
   - `DataProtection:KeysPath`: keeps sign-ins valid across restarts; the folder is created for you, and the app pool needs write access (step 4 gave it).
   The deploy script never overwrites this file, the `keys` folder, or `logs`.
6. **Google sign-in (optional, off until both values are set).** Create a Google Cloud project just for DD Review and an OAuth client of type
   *Web application* with authorized redirect URIs `https://blazor.ddreview.com/signin-google` (and `https://localhost:7004/signin-google` for development).
   Put the Client ID and Client Secret in `appsettings.Production.json` under `Authentication:Google` (locally:
   `dotnet user-secrets set "Authentication:Google:ClientId" "..."` and `...:ClientSecret`). Run `sql\003_central_google_link.sql` on the central master and give the
   central web login `SELECT, INSERT, UPDATE, DELETE` on `dbo.tblWebUserGoogle`. Then each person signs in with their password once, opens their name at the top right (Account)
   and chooses *Link Google account*; after that the *Sign in with Google* button on the login page works for them. Logins are matched by Google's account id, never by email address.
7. **SQL Server.** From this server the central master and every client database that staging will reach must be reachable (firewall, SQL login).
   In each client database the web login needs `grant execute on type::dbo.DrillPair to [<web login user>]` (after running `sql\002_client_drill_pair_type.sql` there).
8. **DNS**: `blazor.ddreview.com` to the server (you are handling this), plus the certificate.

## Deploying (and every later update)

```
cd C:\src\ddreview-blazor
.\deploy\Deploy-Staging.ps1
```
It pulls the checked-out branch, runs the tests, publishes, zips the live folder to `C:\inetpub\backups\blazor.ddreview.com`
(keeps the last 8), puts up `app_offline.htm`, copies the new files, takes the site back online and requests the login page.
Options: `-SkipTests`, `-SkipPull`, `-SiteDir`, `-BackupDir`, `-Url`.

Smoke test afterwards: open https://blazor.ddreview.com, sign in, run Sales and Receipts for a day you know, compare with turbo.ddreview.com.

## Rolling back
Stop the site, unzip the newest good file from the backups folder over `C:\inetpub\blazor.ddreview.com`, start the site:
```
Stop-Website blazor.ddreview.com
Expand-Archive C:\inetpub\backups\blazor.ddreview.com\site-YYYYMMDD-HHMMSS.zip C:\inetpub\blazor.ddreview.com -Force
Start-Website blazor.ddreview.com
```

## Troubleshooting
- **HTTP 500.30 / 500.31 / 502.5:** the Hosting Bundle is missing or was installed before IIS (reinstall it, `iisreset`), or the app threw at start-up. Set `stdoutLogEnabled="true"` in the site's `web.config`, create the `logs` folder, recycle the pool, read `logs\stdout*`, then set it back to false.
- **Sign-in page loads but the page then says it is reconnecting / is blank:** the WebSocket Protocol feature is not installed.
- **Login loops or says unknown database:** check `CentralConnectionString`, that `ClientConnectionStringOverride` is `null`, and that this server can reach the client's SQL server.
- **A page shows "Conversion failed" or a missing-object error from SQL:** the client database is missing a script; compare with `sql\` in the repo.
- Windows Event Viewer > Application shows the ASP.NET Core Module's start-up errors.
