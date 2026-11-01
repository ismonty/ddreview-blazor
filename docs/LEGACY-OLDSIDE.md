# Old-site (VS 2008) side of the handoff: what to build and test

Draft files are in `legacy-side/`. They were written without a compiler (C# 2.0 syntax only), so build and test them in VS 2008.

## What changes in the old site
1. **Add** `legacy-side/LegacyHandoff.cs` to `DDReviewAppCode\App_Code`. It checks the ticket (HMAC-SHA256, one use, 60 seconds), looks up the person's user name and password in the central master by `WebUserID`, and accepts only a `ReturnUrl` inside `/classic/`.
2. **Apply** `legacy-side/LoginPage.handoff.patch` to `DDReviewMarauder\LoginPage.aspx.cs` (an `OnLoad` override; about 15 lines). It fills in the existing login fields and calls the existing `cmdLogin_Click`, so every session value is set by the code that sets it today. `LoginPageClass.cs` is not touched.
3. **web.config** (this server's copy; see `legacy-side/web.config.handoff.txt`): `HandoffSecret` app setting, `path="/classic"` on the forms cookie, the old site's own SQL login, debug off.

## How a visit works
Person signs in to the new site, then opens `/classic-enter?to=/classic/DDSalesReview.aspx`. The new site mints a ticket and redirects to `/classic/LoginPage.aspx?t=...&ReturnUrl=...`. With handoff on, the proxy lets the old login page through only as that GET; the old page signs the person in and Forms authentication sends them to `ReturnUrl`.

## To check in VS 2008
- `LegacyHandoff.TryVerify` against the two vectors in `tests/DDReview.Tests/Legacy/LegacyTicketTests.cs` (secret `test-secret-0123456789abcdef0123456789ab`; use `now` = unix 1789999940 for the first, and 1790000000 for the second). A used ticket must fail the second time.
- `Request.IsLocal` is true behind the proxy (the proxy calls from 127.0.0.1).
- `OnLoad` runs after the base class's `Page_Load` (which clears the session), so the login survives.

## Known dependency
The handoff reads the old plain-text `Password` from `tblWebUserSecurity`. If the new site ever stops keeping that column filled, the handoff must switch to a different step (for example a server-to-server "sign this person in" call that skips the password check); that is a bigger old-side change and is deliberately not done now.

## SQL
`sql/005_legacy_central_access.sql` (central) and `sql/006_legacy_client_access.sql` (each client database) add grant procedures for the old site's own login, same pattern as `sql/004` (lists first, `@Execute = 1` to run). The old site cannot update client databases under this login; use the Database updater.

## Installing the old site in IIS: what we learned (2026-11-01)
- Two separate IIS sites: the new app, and the old site bound to `127.0.0.1:8081` only, with an application named `classic` whose folder holds the old files (here `C:\inetpub\DDReviewClassic`). Never put the `classic` application inside the new app's site.
- Integrated-mode app pool. The old `web.config` needs `<validation validateIntegratedModeConfiguration="false"/>` and, in `<system.webServer><handlers>`, a registration of the chart handler. Without it `ChartAxd.axd` is answered by IIS's StaticFile handler (404) and both dashboard charts are blank:
  `<remove name="ChartAxd"/>` then `<add name="ChartAxd" verb="*" path="ChartAxd.axd" resourceType="Unspecified" preCondition="integratedMode" type="Dundas.Charting.WebControl.ChartHttpHandler, DundasWebChart"/>`.
  Keep the original `<system.web><httpHandlers>` block as it was; the two sections use different formats.
- The server's `web.config` is its own copy (its own SQL login, its own `HandoffSecret`); do not overwrite it with the source copy.
- The Dundas gauges work with no extra handler; the proxy lets `TempFiles/` and `TempImages/` through for `.js`/`.png` only.
- The master page writes `url(~/images/...)` in a `style` attribute, which the browser cannot resolve (a harmless 404 for the tiled background; same on the original site).
- Do not paste real secrets into chats or commit them: the handoff secret exists only in the two servers' config files.
