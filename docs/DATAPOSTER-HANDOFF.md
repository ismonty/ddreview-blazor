# DataPoster (DDQuickBooksPoster): handoff for a new session

Written 2026-11-02 at the end of the session that got the poster running. Read `docs/QUICKBOOKS.md` too (how a run goes, what is copied from Access on purpose). No secrets are in this file.

## What it is
`DDQuickBooksPoster.exe` is the Windows program that takes the general ledger / accounts receivable that DD Review produces and posts it into QuickBooks Desktop (QBXML), or writes IIF / CSV / QBXML import files instead.
It replaces the QuickBooks half of the old Access program DDAdminCS. The website (Blazor) produces the numbers; the poster receives them and talks to QuickBooks. **It must always be able to run on a different computer** from the website:
the end user's own PC or a virtual machine that has QuickBooks Desktop installed and the company file open.

- Code: `src/DDReview.QuickBooks` (library: package reader, plan builder, posting engine, import-file writers, no UI) and `src/DDReview.QuickBooks.Win` (WinForms shell: `MainForm.cs`, `Program.cs`, `StaRunner.cs`, `UserSettings.cs`).
- Target: `net10.0-windows`, **x86 only** (QuickBooks' request processor, QBXMLRP2, is a 32-bit COM server). Tests are in `tests/DDReview.Tests/QuickBooks` and use a fake QuickBooks that answers QBXML the way real logs show.
- Data in: `select * from dbo.fn_CashAndSalesGLandARMultiStoreSelectExportPackage2(stores, start, end, scope, 0, 0)` on the client database (`SqlPackageSource`), scope bits 1 GL, 2 A/R, 16 A/R adjustments.
- Settings: back-end connection string typed once, kept per Windows user in `%AppData%\DDReview\QuickBooksPoster.json`; database settings via `cs_spGlobalSettingsGetNoValue`; optional registry keys under `HKCU\SOFTWARE\Integrated Restaurant Systems\DDEOD\CurrentVersion` (`QuickBooks Debug Directory` turns on `QBGeneralLedgerTransaction.xml` and `QBLogFile.txt`).
- QuickBooks sees the program under the application name `DD Admin CS` (`Qbxmlrp2Processor` default). **QuickBooks remembers its "allow this program" decision per application name**, so changing the name makes every user authorize it again.

## State at hand-off
- Works against a real QuickBooks Desktop 2019 with a blank company file (it connects, the access dialog appears, the program runs). The owner's experience over thousands of batches with the old system: if the posting balances and totals in QuickBooks, it is right, and QuickBooks refuses an import that does not balance.
  The full Preview, Post, Preview-again test on a company file with data will be done when the owner can arrange it; they expect it to simply work.
- Bug fixed this session: the program looked for the COM name `QBXMLRP2.RequestProcessor2`; QuickBooks registers `QBXMLRP2.RequestProcessor`. The lookup now tries both (and `.1` variants) and the error lists the names tried and whether the process is 32-bit. (Pull request: branch `fix-qbxmlrp2-progid`.)
- Cannot be tested from the cloud workspace: the QuickBooks connection itself. The cloud workspace has no Windows, no QuickBooks and no VS 2008; everything real is checked by the owner.

## How a build is delivered today (no installer yet)
From a checkout of `main` (any machine with the .NET 10 SDK; the cloud workspace can do it too):
```
dotnet publish src\DDReview.QuickBooks.Win\DDReview.QuickBooks.Win.csproj -c Release -r win-x86 --self-contained true -o <folder>
```
Delete the `.pdb` files, zip the folder (about 47 MB), unblock the zip on the target machine, unzip anywhere, run `DDQuickBooksPoster.exe`. Nothing to install or register; the target machine needs QuickBooks Desktop (opened as Admin on the company file the first time), network access to the SQL Server and a SQL login that can read the client database and run the export function.
A `deploy\Publish-Poster.ps1` that does the build and zip in one step was offered and is not written yet.

## Status of the launch-from-the-website work (2026-11-02)
Built on branch `poster-launch-link`: the `ddposter://` link (signed, short-lived, validated on both sides), the **Post to QuickBooks…** button on `/gl`, the poster's argument handling, per-user registration,
single-instance forwarding, store limits for link sessions, the day-0 check, and `tools/DDReview.PosterKeyTool`. Setup and rules are in `docs/QUICKBOOKS.md` ("Opening the poster from the website").
Chosen with the owner: signed links; an unverified link opens the poster with nothing filled in and says why. Still to do, in this order: a downloadable `.ddposter` file as a fallback for PCs that block URL protocols
(same signed payload, same validator), then ClickOnce (needs a code-signing decision), then the small clean-ups below. Not yet checked on a real Windows PC and browser: the one-click flow itself.
The text below is the original brief, kept for the reasoning.

## First thing to do next (original brief): launch the poster from the website with parameters set
Wanted: on the Blazor site (for example on the General Ledger page, or a "Post to QuickBooks" button) the user clicks once and the poster opens on their computer with the **date range, scope (GL / A/R / adjustments), stores** and other options already filled in, ready to Preview (and Post). The poster may run on another computer than the browser's server, so the website cannot start it; the browser on the user's own PC must.
Options to weigh (decide with the owner, then build the site side and the poster side together):
1. **Custom URL protocol** (for example `ddposter://run?...`). The poster registers the scheme under `HKCU\Software\Classes` (no administrator needed, per user, from a "register" switch or first run) and parses the arguments. The website button is a link. The browser asks once whether to open it. Works from any browser on that PC, including when the poster is not running yet (start it) or is (pass it on to the running instance, for example a named pipe or single-instance mutex).
2. **A small launch file the site downloads** (for example `.ddposter`, JSON) that is associated with the program: works where URL protocols are blocked, but needs a file association and one more click.
3. ClickOnce / MSIX app installer: handles install and update too, but is a bigger change and adds a signing question.
Design points to settle early:
- **Never put the connection string or any password in the link.** Parameters only: start date, end date, scope, store ids, maybe a label. The poster still uses its own saved connection string (per Windows user), or later a login to the website to fetch the package.
- **Validate every parameter in the poster** (dates in range, scope bits known, store ids digits only). Treat the link as untrusted input: anyone can craft one. Preview must remain the default; Post should still need a deliberate click (do not auto-post from a link).
- **Store restriction:** the poster uses a SQL login, not the website user, so the site's per-user store permissions are not enforced by the poster. Decide whether the link carries a short-lived signed token (HMAC from the site, checked by the poster, with an expiry) and what the poster does if it cannot verify it; or whether the SQL login's own rights are the limit. (The old-site plan in `docs/LEGACY-PROXY-PLAN.md` uses a similar signed handoff; reuse the idea.)
- **Single instance** and a clear message if QuickBooks is closed or the company file is not open as Admin.
- **Defaults and "last run" memory** so the same click each month is repeatable.

## Other small things to clean up (the owner's list for the new conversation)
- A `Publish-Poster.ps1` (build, strip pdbs, zip) and a short end-user install note.
- Show the version/build in the window title so it is obvious which build is on which PC.
- Make the debug folder and log location easy to find from the window; keep the connection string out of any log.
- Not ported yet (on demand): vendor bills and credits (A/P), payroll (scope bit 4), COGS (bit 8), beginning balances, club-minimum invoices, QuickBooks Online, Canadian tax beyond copying the old settings, other accounting packages (`PostingPlan` is neutral, so another package is a second writer beside `QbxmlRequests`).
- "Copied as it was, do not fix without asking" list is in `docs/QUICKBOOKS.md`.

## Resources (the owner's computer, reachable when the session is linked to it)
`C:\EOD\ddreview-blazor` (checkout), `C:\EOD\DDReviewBlazorPort` (`DDAdminCS-ExportedCode.zip`: the Access source with the original QBXML posting logic; `QBExportPackage.csv`, `QBPostResults*`: sample package and results; schema dumps; test `.bak` files).
Repo: private GitHub `ismonty/ddreview-blazor`, default branch `main` (staging auto-deploys from it). Commit trailers (Co-Authored-By, Claude-Session) are fine in commits; app text, README and PR text stay the owner's own.

## Next project: the poster fetches its package from the website (first step built: the signed .ddpackage file, see docs/QUICKBOOKS.md; the live fetch below is not)

Today the poster reads the export package straight from the back-end database with a SQL login whose connection string is typed into the poster and kept (now encrypted per Windows user, see `ProtectedSetting`) on every PC.
A link's store list is therefore a convenience, not a wall: a hand-launched poster with that login can read any store the login can.

Plan: the poster stops using a database login.
1. The signed link (or launch file) already carries the stores, dates and scope. Add a one-time `jti` (random id) and keep the 10-minute lifetime.
2. New website endpoint, e.g. `POST /poster/package`, takes the link text; the site verifies its own signature and expiry, marks the `jti` used, re-checks the web user's store permissions, runs the same query the poster runs today, and returns the package rows (JSON, over HTTPS).
3. The poster posts the link to the site from the PC (it only needs to reach the website), reads the package from the answer, and keeps everything after that (plan, Preview, Post, QuickBooks) unchanged. `SqlPackageSource` becomes one of two package sources.
4. The direct-database mode stays as an optional "advanced" mode for the owner's own PC; the connection box is hidden unless it is switched on.
5. Day-0 check and the posting settings (`LoadOptions`) also move to the endpoint.
Open questions: whether the endpoint needs the user's session as well as the link (the link alone is already a signed, short-lived grant); size limits for a year of data; how the poster reaches the site when it is only on an internal network.
