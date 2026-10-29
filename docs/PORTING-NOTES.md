# DDReview Blazor port - working notes

Target: .NET 10, Blazor Web App (Interactive Server). Live site being replaced: turbo.ddreview.com (the `DDReviewMarauder` web site in the old solution).

## Layout
- `src/DDReview.Core` - login, tenant connection, GL service, support code (no UI).
- `src/DDReview.Web` - Blazor site: cookie login, GL review page.
- `tests/DDReview.Tests` - unit tests. Set `DDREVIEW_GL_SAMPLE` to a GL export CSV to run the golden-master balance test.
- `sql/` - scripts to run by hand against the **test** databases first.

## Configuration (never commit real values)
Use `dotnet user-secrets` for `Tenancy:CentralConnectionString`. For testing against a restored copy set
`Tenancy:ClientConnectionStringOverride` so every login uses the test client database instead of the connection string stored in central.

## How the old login works (mirrored here)
1. Central master `tblWebUserSecurity`: user name or e-mail plus password (case-insensitive, plain text in the old app) returns the client's connection string, client-side user and password, license data.
2. Client database `tblWebUserSecurity` + `tblWebUserSecurityStores`: per-report store authorization.
New app: parameterized queries, PBKDF2 hashes in a separate table (legacy passwords upgrade on first successful login), login throttling, antiforgery, secure cookie.

## Decisions to confirm
- GL page is gated on the Debit/Credit report authorization (or client Administrator). Confirm this is the right permission.
- Meaning of each bit of the `@ARFlag` argument to `fn_CashAndSalesGLandARMultiStoreSelect` (2 = standard, as in the sample).
- The GL page is read-only. QuickBooks (QBXML) and Dynamics exports stay in the Access front end.

## Sales Review plan
The logged scripts are one ~65 KB template with ~25 swap points per dimension (99% shared text between variants). Plan: one embedded
T-SQL template with typed substitution points, values passed as parameters, verified by running each of the logged scripts and the generated one against a restored database and diffing results.
Dimensions never seen in 11,175 logged scripts (deferred): FrequentDinerPlan, FrequentDinerCustomer, CostAccount, CostTrackingItem.

## Slice 2: Sales Review
- `ARFlag` for the GL functions: 1 = GL, 2 = AR, 3 = both (confirmed by the owner).
- `src/DDReview.Core/Sales/Legacy` is the old script generator, kept nearly verbatim behind `ISalesLookups`.
  `SalesScriptCharacterizationTests` regenerates all 71 production scripts logged in `tblWebLoginScripts`
  and requires a whitespace-normalized match (set `DDREVIEW_SALES_SCRIPTS` to the exported folder).
- The old VS2008 source differed from the deployed build in three places; the logged scripts won:
  royalty-override update block, no `StoreStaffListCSV` term in the StoreID select/group-by, no pseudo
  profit-center union in Marauder mode.
- `SalesService.Parameterize` turns the header `set @X=literal` lines into SqlParameters. List values must match
  `^[0-9,:\-]*$` (the generator still inlines lists in the body), and store ids are intersected with the user's
  authorized stores before the script is built.
- Not yet verified against a live database from the cloud workspace: run `/sales` locally against the restored test copies.

## Slice 3: Receipts Review
- Generator: `src/DDReview.Core/Receipts/Legacy/DataAccessReceiptsReview.cs` (from `DDReviewAppCode/App_Code`, DB/session helpers removed).
  `ReceiptsScriptCharacterizationTests` reproduces a captured live script exactly (set `DDREVIEW_RECEIPTS_SCRIPTS`; add more .sql files to extend).
- `ReceiptsService` probes the client DB for the `ProfitCenterID` column and the "Cash Tips Earned" detail id (same probes as the old login), asks for deposit detail only when a detail dimension is used.
- `ReceiptsLayout` defines the 10 stacked columns of the old grid; a test checks every value against the live result row and screenshot.
- Still to confirm on a live database: `/receipts` against the restored Valentini's copy (week of 10/21-10/27 should show 35,618.81 expected).

## Look and feel
The original DD Review colours, type and layout were carried over by the owner into the "Twin Ports (Main)" style that exists in the MOOTT and BOB applications. This app uses the same variable names and values as that style's default block (`--bg`, `--card`, `--tile`, `--ink`, `--header`, `--primary`, `--gold` ...) and the same self-hosted Roboto, so end users of the old site see a familiar look and the apps can share one theme. To restyle, change the variables at the top of `src/DDReview.Web/wwwroot/app.css`.

## Slice 4: Labor review (unverified against production)

- `Labor/Legacy/DataAccessLaborReview.cs` is the old generator with the ADO/Retrieve methods removed; `LaborService` configures it from a `LaborRequest` and runs the script with the 22 header `set` lines turned into parameters (shared `SalesService.Parameterize`).
- Punch detail implies Employee, as the old button did. The old header script sets `@EndDayNumber` from the start day; that quirk is kept, only the data script is run.
- `LaborLayout` follows `LaborReviewPage`: dimension columns, optional Date/Start/End punch columns, Mon..Sun per display mode (Hours, Dollars, Both, Verbose = adds sales and labor %), Total Hours / $ / Sales / Labor %, OT and DT hours.
- **No captured production scripts exist for labor** (the old labor page never logged them). Until the Extended Events capture is done on the production server, the generator is a source-faithful port with no byte-for-byte check. Add a characterization test like the Sales one when scripts arrive.
- Not ported yet: the header block above the grid, Excel, Previous/Next, drill, order-by, and the last three total controls (`tct[13..15]`) whose column mapping in the old code looks unused.

## Slice 5: the home tabs (unverified against the live database)

Source: `DDReviewMarauder/Default.aspx(.cs)` and `App_Code/DefaultCommon.cs`. Every number comes from client-database SQL functions; the port only calls them with real parameters:
`fn_DashBoardAlerts`, `fn_DashBoardSummary` (This Week / Last Week), `fn_DailySummaryByStore` / `ByWeekDay` / `ByCalendarDay`, `fn_StoreRegisterProcessingStatus`, and `cs_spDailySummaryByStoreCheckPreTotalSales` (the old page ran it before the summary; it is called best-effort).
Store list = the stores allowed for Sales (old `StoreListAuthorizedSales`). Default dates: Dashboard and Daily Summary = yesterday vs 364 days back; This Week / Last Week = Monday..Sunday.

Differences from the old screens, on purpose:
- Dundas gauges and charts are plain SVG (`SvgGauge`, `BarChart`, `BulletBar`); same data, similar face.
- Daily Summary: the old page showed Day Part 1 as a ratio (sales / last year's, e.g. 1.58) instead of the amount, and zeros for the Day Part totals on the total row. The port shows the amount, and sums the day parts in the total row.
- Not ported yet: the Options and Downloads/Payments tabs, the user's saved tab labels and default date ranges (`DateRangePickerDefaults`), the Affordable Care Act alert, and the quick gauge.
