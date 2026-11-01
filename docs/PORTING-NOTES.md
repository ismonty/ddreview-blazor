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
- Not ported yet: the header block above the grid, Excel, Previous/Next, and the last three total controls (`tct[13..15]`) whose column mapping in the old code looks unused.

## Slice 5: the home tabs (unverified against the live database)

Source: `DDReviewMarauder/Default.aspx(.cs)` and `App_Code/DefaultCommon.cs`. Every number comes from client-database SQL functions; the port only calls them with real parameters:
`fn_DashBoardAlerts`, `fn_DashBoardSummary` (This Week / Last Week), `fn_DailySummaryByStore` / `ByWeekDay` / `ByCalendarDay`, `fn_StoreRegisterProcessingStatus`, and `cs_spDailySummaryByStoreCheckPreTotalSales` (the old page ran it before the summary; it is called best-effort).
Store list = the stores allowed for Sales (old `StoreListAuthorizedSales`). Default dates: Dashboard and Daily Summary = yesterday vs 364 days back; This Week / Last Week = Monday..Sunday.

Differences from the old screens, on purpose:
- Dundas gauges and charts are plain SVG (`SvgGauge`, `BarChart`, `BulletBar`); same data, similar face.
- Daily Summary: the old page showed Day Part 1 as a ratio (sales / last year's, e.g. 1.58) instead of the amount, and zeros for the Day Part totals on the total row. The port shows the amount, and sums the day parts in the total row.
- Not ported yet: the Options and Downloads/Payments tabs, the user's saved tab labels and default date ranges (`DateRangePickerDefaults`), the Affordable Care Act alert, and the quick gauge.

## Slice 6: footer buttons

Previous / Next (shift the date range by its own length and rerun), Hide/Show Weekdays (Sales and Labor), and Download as Excel on Sales, Receipts and Labor. The workbook comes from `XlsxWriter` (hand-written minimal .xlsx, no package; checked by opening a sample in LibreOffice). Receipts exports each stacked line as its own column. Not ported yet: Link/Print, Specials and Daily Discounts buttons, Export Daily Sales, the L0..L3 and LY buttons.

## Slice 7: drill and order-by (Sales, Receipts, Labor)

Source: `ReviewPage.cs` ClickState / ReviewControlColumnItemTemplate / HeaderColumnClass, and the control-column tables of the three review pages. Code: `Core/Drill/Drill.cs` (state, filters, sort, name lookup) and `DrillCatalogs.cs` (one table per review: dimension, display columns, id column, next dimension, name function). UI: `DrillHeader.razor` plus the three pages.

Behaviour kept from the old grid:
- Clicking a **row cell** of a dimension sets that dimension's filter to the row, turns that dimension off and turns on its "next" dimension (`ClearFlagAndSet`). Filters of other dimensions stay.
- Clicking a **header title** shows only that dimension, filters kept (`ClearFlagsAndSet`).
- Header buttons: **Up** (Drill Up: add this dimension), **Right** (Drill Right: add the next one), **All** (Show All: drop this dimension's filter; shown only while a filter is on).
- Same-named things in several stores (menu items, discounts, staff, ...) are clicked by name; the id list comes from the client database's own `fn_ParsePairSetStringAdd*` function, as before (parameterized here; if it fails or returns anything but digits/colons/spaces the plain id is used).
- The Run/Submit button starts a fresh report (filters and sort cleared); Previous/Next keep them.

Changes and decisions:
- **A Store filter can only narrow the stores the user already has** (`DrillFilters.Split`); it never reaches the generator's store list as typed. Unit-tested.
- **Order-by**: number headers sort on click (high to low first, again to reverse); dimension columns have a small sort button (their title click is the drill, as in the old grid); sorting uses the day/week/hour number rather than the text where the old grid did. Receipts' stacked headers sort per line. Percent columns sort by the ratio.
- Not done: the saved per-column sort strings and the "Back/Forward" click-state history buttons; the "Forget This" header block that listed the active filters by name (a one-line "Filtered on" list with a clear button replaces it).
- Unverified against a live database: the result tables are expected to carry the id columns named in the catalog (`StoreID`, `MainTypeID`, ...). A cell whose id column is missing is simply not clickable, so the failure mode is "no link", not a wrong filter. Check on turbo which rows are clickable.

Follow-up (row check boxes): every row has a check box. Clicking a dimension cell uses the clicked row plus all checked rows (clicked first, each once): ids are joined, and name-looked-up dimensions feed each name through the client function with the list so far, as the old grid did. Checks clear on every new run. Cells of name-looked-up dimensions (Sale Type, Menu Item, Discount, staff, ...) are clickable by name; before, they wrongly needed the id column, which is empty for them.

## Slice 8: QuickBooks posting (new projects)
See `docs/QUICKBOOKS.md`. `DDReview.QuickBooks` (library) and `DDReview.QuickBooks.Win` (x86 WinForms) post GL and A/R into QuickBooks Desktop,
or write IIF/CSV/QBXML files. Tests: plan golden-tested against the October Access plan (sanitised store and tax names), A/R cases with synthetic customers,
engine against a fake QuickBooks (preview, first post, re-post unchanged, modify, parent account types, missing references), import-file balance checks.
Publish: `dotnet publish src/DDReview.QuickBooks.Win -c Release -r win-x86 --self-contained` (x86 is required).

## Slice 9: drill filters as a table-valued parameter
The name-looked-up filter lists ("1,2,3:8901,8902 1,2:8903 3:8904": stores with items, because one name has different ids per store) used to be pasted
into the generated SQL as OR chains by the three `ParsePairSetOutput` methods. Now, while a `PairScope` is active (`Drill/PairSets.cs`), they emit
`exists (select 1 from @DrillPairs dpN where dpN.Dim = N and dpN.StoreID = <store col> and dpN.ItemID = <item col>)` and the pairs travel in one
table-valued parameter of type `dbo.DrillPair` (script `sql/002_client_drill_pair_type.sql`, run once per client database). `fn_ParsePairSetStringAdd*` are untouched and still
turn a clicked name into ids; only their text result is parsed in C# instead of pasted. Without the type in the database, or outside a scope (tests, diagnostics),
the old text is generated, byte for byte. Lists without a colon stay `item in (...)`.
Unverified against a live database: run the script on a test client DB, drill a menu item across several stores, and compare totals with the text version
(drop the type to compare).

## Slice 10: submission reports (General Ledger)
Source: `DDReviewMarauder/DDAdminGLARSubmissionReportGraphic.aspx.cs`. The register software stores a page for each posted batch as drawing commands in `tblCashAndSalesReport`
(1 text, 2 bold on/off, 3 line, 4 right-aligned text, 5 centered text; positions in twips). The old page replayed them into an 8.5 x 11 inch JPEG with System.Drawing.
Port: `Core/Gl/SubmissionReports.cs`. `SubmissionReportService` lists batches (store, date, only those that have stored commands) and reads one batch; both are plain SQL, parameterized,
and limited to the stores the user's General Ledger access covers (the check is in the SQL, so a typed batch number for another store returns nothing). `SubmissionSvg` replays the commands as SVG
(816 x 1056 px, Verdana 7 pt; twips / 15 = px, as before). The image comes from `GET /gl/submission-image/{batchId}` (signed-in users only, cached privately for 10 minutes).
UI (`Gl.razor`): a "Submission report" link on each daily total line of the detail modes opens that day's reports (straight to the page when there is only one);
a "View Submission Reports" button under the results opens all reports of the run as thumbnails (store and date in small text), tap one for the full page with Previous/Next.
The viewer opens over the page so the report you ran stays put. Thumbnails load lazily, 48 at a time. At most 1000 reports are listed per request.
Quiet days (no sales line with an amount or quantity and no receipt line with an amount) are left out of the list; an older schema missing one of those columns gets the list without that test.
Unverified against a live database: whether the text sits exactly where the old JPEG put it (the baseline offset is an estimate), and the SQL on a database without `tblCashAndSalesReport` (shows a "not available" message).

