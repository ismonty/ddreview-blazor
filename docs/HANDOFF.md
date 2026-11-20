# Handoff notes: porting DDReview to Blazor

Written at the end of a long working session so a new session (or a new person) can pick up quickly. No secrets are in this file;
connection strings, passwords and OAuth secrets live only in user-secrets / `appsettings.Production.json`.

## What this is
DDReview is a 20+ year ASP.NET WebForms reporting site for Digital Dining restaurant POS data (live at turbo.ddreview.com; the live site is the
`DDReviewMarauder` folder). This repo is the port to Blazor Web App (Interactive Server) on .NET 10, keeping the original look ("Twin Ports" colours)
and keeping the SQL logic where it is (the old C# review pages generate very dynamic SQL; the port keeps those generators and calls them).
Every customer has their own client database (1 to ~150 stores); a central master database maps each web login to its client connection string.

## Repo state
- GitHub: `ismonty/ddreview-blazor` (private). Branches: `main`, `slice-1-login-gl`, `slice-2-sales-review` (all later work; pull request #1 into `main`, **not merged yet**).
- Local checkout used by the owner: `C:\EOD\ddreview-blazor`. Build: `dotnet build DDReview.slnx`; tests: `dotnet test tests/DDReview.Tests` (85 passing at last run).
- Run locally: `cd src\DDReview.Web` then `dotnet run --launch-profile https` (https://localhost:7004). Settings come from user-secrets
  (`Tenancy:CentralConnectionString`; leave `Tenancy:ClientConnectionStringOverride` unset or every login is sent to that one database).
- Commit trailers the owner allows: `Co-Authored-By: Claude ...` and `Claude-Session: ...`. The public face (app text, README, PR text) stays the owner's own.
  Do not commit real customer names or data (test fixtures are sanitised).

## Layout
- `src/DDReview.Core`: tenancy/login (`Tenancy/`), legacy SQL generators (`Sales/Legacy`, `Receipts/Legacy`, `Labor/Legacy`; ported verbatim, change with care),
  services (`SalesService`, `ReceiptsService`, `LaborService`, `GlService`, `HomeService`), drill model (`Drill/`: `DrillState`, `DrillCatalogs`, `DrillKeyResolver`, `PairSets.cs`), layouts, `XlsxWriter`.
- `src/DDReview.Web`: Blazor pages (`Components/Pages`: Home, Sales, Receipts, Labor, Gl, Login, Account), shared components (`Components/Home`: `MultiSelect`, `DrillHeader`, `ReportButtons`), `wwwroot/app.css` (one stylesheet).
- `src/DDReview.QuickBooks` (+ `.Win`): QuickBooks Desktop posting (QBXML) and IIF/CSV files; see `docs/QUICKBOOKS.md`.
- `sql/`: numbered scripts for the *new* objects only: 001 central password-hash table, 002 `dbo.DrillPair` type (per client DB), 003 central Google link table, 004 `dbo.usp_DDReviewGrantAccess` (per client DB: every grant the web login needs in one procedure; lists only unless `@Execute = 1`).
  Existing database objects (e.g. `fn_ParsePairSetStringAdd*`, `fn_CashAndSalesGLandARMultiStoreSelect*`, used by the Access presentation layer DDAdminCS) must stay intact.
- `deploy/` + `docs/DEPLOY-STAGING.md`: IIS staging deployment (blazor-staging.ddreview.com), built on the server from git.
- `docs/PORTING-NOTES.md`: the slice-by-slice log of what was ported, decisions, and what is unverified. Read it first.

## What is ported
Login (central master, legacy password upgrade to hash, throttling, redirect accounts) and Google sign-in (link from the Account page; matched by Google account id);
Home tabs (Dashboard, Daily Summary, This/Last Week, Alerts, Reports); Sales, Receipts and Labor reviews with drill, sort, Previous/Next, weekdays toggle, Excel download,
totals rows, multi-select dropdown pickers (active stores only, `tblStores.Active > 0`); General Ledger report (zero lines hidden, days grouped with daily and grand totals, banner only when out of balance);
QuickBooks poster; drill filters as a table-valued parameter.
Added after the first handoff: Daily Summary / This Week / Last Week leave out all-zero stores and sections; General Ledger modes are Summary, Detail with balance check,
Totals by account and reference (date and batch ignored, date range shown, balance check), and Totals at the top with detail below (the old Mode drop-down unselected itself because
Blazor drops a `false` option value; modes are now plain strings); a **Database updater** page (`/admin/updater`, client-database administrators; linked from the Account page) that checks a client
database for the objects DD Review adds (scripts 002 and 004, embedded from `/sql`, registered in `src/DDReview.Core/Updater/ClientScripts.cs`) and adds the missing ones. It only adds, never grants
itself rights: if the reports' login lacks db_ddladmin it shows the `sp_addrolemember` line to run, and the grant procedure covers the grants.

## Not ported / open
- Submission reports are ported (slice 10 in PORTING-NOTES) but not yet seen on staging.
- Downloads/Payments tab (owner said hold), Options tab, Link/Print, Specials/Daily Discounts, L0-L3/LY buttons, Costs review, saved per-column sort strings, Back/Forward click history.
- Receipts "Credit Card/Cash" conversion error on an older-schema client DB was fixed (ReceiptClass key column name); the class/type cell not being a link there needs re-checking after the fix.
- Labor: crash on empty result fixed; employee click now opens punch detail; a client DB where Labor returned no rows at first still needs a look (send the generated script and run in SSMS to find out why).
- QuickBooks poster has only been validated against a fake QuickBooks and one sanitised plan; the owner has not yet run it against a real QB 2019 test company (Preview, Post, Preview again).
- Drill-pair TVP and Google sign-in are unverified against live data/Google; the first real runs are the test.
- **Staging is deployed and running** at https://blazor-staging.ddreview.com (IIS app pool `ddreview-staging`, site folder `C:\inetpub\blazor-staging.ddreview.com`, built on the server in
  `C:\src\ddreview-blazor` by `.\deploy\Deploy-Staging.ps1`). First-deploy problems and fixes are in `docs/DEPLOY-STAGING.md` (locked `<webSocket>` line, `keys` folder permissions,
  JSON escaping, SQL login needing a user in the central master with its grants). The server's SQL Server is the default instance (no `\SQLEXPRESS`) and needed SQL authentication on.
- Still to verify on staging: Google sign-in round trip (link from the Account page first; needs the OAuth client for DD Review with redirect `https://blazor-staging.ddreview.com/signin-google`,
  `Authentication:Google:ClientId/ClientSecret` in `appsettings.Production.json`, sql 003 on the central master with grants); sql 002 and 004 in each client DB (use the Database updater, then
  `exec dbo.usp_DDReviewGrantAccess @UserName = N'<web login user>', @Execute = 1;`). The deploy script's final self-check cannot reach the public name from the server (only a warning).
- Not tried on staging yet: the Database updater itself, the new GL modes, the all-zero skipping (all pass tests only).
- Everything is merged into `main` (PR #1 and the dashboard PR #2). `deploy\Deploy-Auto.ps1` + `Register-AutoDeploy.ps1` (staging auto-deploy from `main`, docs/DEPLOY-STAGING.md) and `deploy\Deploy-Production.ps1` (manual, only a commit staging runs; docs/DEPLOY-PRODUCTION.md, https://blazor.ddreview.com) are written but not yet run on the server.

## The old site behind the new one (built and working, 2026-11-01)
Done and merged: the old WebForms site runs on the server as IIS site `ddreview-classic` (127.0.0.1:8081 only, application `/classic`) and the new app proxies it at `/classic` after sign-in
(YARP; `LegacyProxy` section in `appsettings.Production.json`, off unless `Enabled`). A signed one-time ticket (`/classic-enter`) gives one sign-in; there is a Classic link in the top nav and a
"signed out of Classic" page. Design and install lessons: `docs/LEGACY-PROXY-PLAN.md`, `docs/LEGACY-OLDSIDE.md` (what is built in VS 2008). The owner builds the old-side code; Claude cannot compile it.
Still open:
- Least-privilege SQL for the old site (`sql/005`, `sql/006`, login `ddreview_classic`): run on the central master and Valentini's. Not yet run on the other client data server (owner needs its admin password).
  The old site still connects with its original login; switching its `web.config` to `ddreview_classic` is not done. Unconfirmed: whether client pages take their login from the connection strings stored in the central master.
- Check the owner's VS 2008 handoff edits (`LegacyHandoff.cs`, csproj line, `LoginPage.aspx.cs`) into their own source control.
- Not yet looked at on staging: stacked Discounts on the dashboard/Daily Summary charts, header width on a phone, zero-day submission reports left out, the signed-out page, friendly store names (`StoreReportName`) in dropdowns.
- Optional: the old master page background `~/images/...` does not load through the proxy.
Store names: `StoreName` is the licensed hard lock name (it is on the stored submission report picture); `StoreReportName` is the display name, shown almost everywhere (`StoreLookup.NameSql`).

## Resources the owner has (on the owner's computer, reachable when the session is linked to it)
- Original source: `C:\Users\ismon\source\repos\DDReview` (solution `DDReviewDev.sln`; `DDReview\` and `DDReviewMarauder\` web projects, `DDReviewAppCode\App_Code` shared classes:
  `ReviewPage.cs`, `SalesCostsReviewPage.cs`, `ReceiptsReviewPage.cs`, `LaborReviewPage.cs`, `DataAccess*Review*.cs`, `BasePage.cs`, `LoginPageClass.cs`).
  `DDReviewMarauder\Default.aspx(.cs)` generates the home tabs.
- Test and reference material: `C:\EOD\DDReviewBlazorPort` holds `.bak` files of a client database and the central master (restored locally as `_Test` copies on SQL Express),
  schema scripts (`ValentinisSchema`, `DDReview-CentralMaster`: schema-only dumps), `DDAdminCS-ExportedCode.zip` (the Access presentation layer source, which contains the QBXML posting logic),
  captured generated-SQL corpus CSVs and `scripts\` (for characterisation tests; set `DDREVIEW_SALES_SCRIPTS` / `DDREVIEW_RECEIPTS_SCRIPTS` / labor equivalents to run them),
  `QBExportPackage.csv` and `QBPostResults*` (QuickBooks plan/results).
- Machines: the owner's dev PC (SQL Express, Visual Studio, QuickBooks Desktop 2019) and the monty.social IIS server (Windows Server 2025; also hosts MOOTT and Bob, other apps of the owner; keep separate app pools, settings and keys).
- A new session starts with an empty cloud workspace: clone the repo (`add_repo`), link the session to the owner's computer, and ask to stage files from the folders above.

## Conventions worth keeping
- Business rules stay in SQL; C# stays thin. New database objects go in new numbered scripts that are safe to re-run (target SQL Server 2012+).
- Work on a branch, keep the build at 0 errors/warnings, add tests; don't deploy or run SQL against live databases from a session.
- Razor gotchas: do not name a variable `section`; use `<text>` blocks carefully.
- A zero-row result must not crash a page (guard totals and lookups).
