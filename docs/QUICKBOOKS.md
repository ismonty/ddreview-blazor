# QuickBooks posting (GL and A/R)

Port of the DDAdminCS (Access) feature that posts the multistore general ledger and accounts receivable into QuickBooks Desktop.

## Projects
- `src/DDReview.QuickBooks` - the library. No UI. Works on any platform except the real QuickBooks connection.
- `src/DDReview.QuickBooks.Win` - Windows Forms program `DDQuickBooksPoster.exe` (**x86**: QuickBooks' request processor, QBXMLRP2, is a 32-bit COM server).
- `tests/DDReview.Tests/QuickBooks` - tests, including a fake QuickBooks that answers QBXML the way QuickBooks does.

## How a run goes
1. **Read** the export package: `select * from dbo.fn_CashAndSalesGLandARMultiStoreSelectExportPackage2(stores, start, end, scope, 0, 0)` (the last two are the sales-tax flag and account class, which the old app also passed as 0) (`SqlPackageSource`).
   Scope bits as in Access: 1 general ledger, 2 accounts receivable, 16 A/R adjustments (4 labor and 8 COGS are not ported yet).
2. **Plan** (`PlanBuilder`): the package rows become list objects (vendors, accounts, classes, service items, customers, customer types)
   and transactions (journal entries, invoices, receive payments, credit memos). This is a port of
   `QBPostTransactionCreateGeneralLedgerTransaction`; the October package gives the same 53 checks as the plan Access saved
   (`GoldenPlanTests`). The builder sorts rows by the original `ORDER BY` itself.
3. **Preview** (`PostingEngine.Run(plan, commit: false)`): asks QuickBooks whether each object and transaction exists. Changes nothing.
4. **Post** (`commit: true`): creates missing objects (parents before children), adds transactions that are not there, modifies ones that
   differ, and leaves identical ones alone. Order: objects, journal entries, invoices, receive payments (auto-apply), credit memos.
5. **Files instead of posting** (`ImportFiles`): IIF, CSV and QBXML written from the same plan ("Save import files...").

## Differences from the Access program (on purpose)
- Unchanged transactions are recognised by comparing values, so a second post of the same period sends nothing. Access re-sent every journal entry and payment.
- Preview never creates anything. Access created accounts while previewing.
- Names go in as text and XML escaping is done by the XML library (Access escaped by hand; it refused non-ASCII).
- Failures are collected and shown at the end; Access stopped at the first message box.

## Copied as it was (do not "fix" without asking)
- A new account whose parent does not exist is created with the type the package gives; a child of an existing parent takes the parent's type
  (so `0100: NBC Checking Main` came out as Income in the sample - that is what the package said).
- Club-minimum figures live in unused customer fields: AltPhone (expected), AltContact (remaining), ResaleNumber and Fax (spent).
- Invoice reference is `Abs(CheckNumber)-(TransactionNumber mod 10000)`; journal entries use `QuickBooksRefNumber` (for example `DD8397`).
- The "A/R Posting" account and item names come from the package, not from this program.

## Settings it reads
- Back-end database: connection string (typed once, kept per Windows user in `%AppData%\DDReview\QuickBooksPoster.json`).
- Database settings through `cs_spGlobalSettingsGetNoValue`, as Access did: `Quickbooks.AccountsReceivable.Export` (`ExportPurchaseDetails`,
  `ExportPurchaseDetailsInInvoiceMemo`, `ExportInvoiceItemSalesTaxCode`) and `Quickbooks.GeneralLedger.Export` (the Canada tax item and entity).
- Registry (read only), `HKCU\SOFTWARE\Integrated Restaurant Systems\DDEOD\CurrentVersion`: `QuickBooks Debug Directory` (non-empty turns the debug files on:
  `QBGeneralLedgerTransaction.xml` and `QBLogFile.txt`) and `QuickBooks QBXML Max Version`.
- QuickBooks Desktop 2019 takes QBXML up to 14.0. Queries use 4.0 and transactions 6.0 (8.0 for tax lines), as Access did; `EngineOptions` changes that.

## Opening the poster from the website ("Post to QuickBooks" button)
On the General ledger page, **Post to QuickBooks…** opens the poster on the person's own computer with the stores, dates and choice (GL, A/R or both) already filled in.
The browser asks once whether to open the program. The poster may be on another computer than the website, so the browser on the user's PC does the launching, with a `ddposter://` link.

- **Link:** `ddposter://run?v=1&start=2026-10-01&end=2026-10-31&scope=3&stores=4,7,12&exp=<unix>&kid=<key id>&uid=<web user id>&sig=<signature>`.
  Parameters only. Never a connection string, password or user name. The poster still uses its own saved connection string.
- **What a link does:** fills in the form. It never starts Preview and never posts. Post still needs a Preview of the same request and the confirmation question.
- **Rules (`PosterLinkRules`, enforced by the site when it makes a link and again by the poster):** no date before 2003-09-29 (day 0), at most 366 days, end date not more than a year ahead,
  scope 1, 2 or 3 only (A/R adjustments, 16, are not offered from links), 1 to 200 store ids of at most 7 digits, in ascending order. A database may define a later day 0 (`dbo.fn_CalcDayNumberDate(0)`);
  Preview and Post check that too, for every run and not only from links.
- **Signing:** ECDSA P-256. The site holds the private key in its secret settings; the poster holds only public keys in `DDQuickBooksPoster.keys.json` next to the exe
  (published from `deploy/poster-trusted-keys.json`). A link lives 10 minutes (`PosterLink:LifetimeMinutes`, at most 30) and the poster allows 5 minutes of clock difference.
  A link that is unsigned, signed by an unknown key, altered, expired or outside the rules changes nothing: the poster says why and stays as it was.
- **Stores:** the link carries only the stores that user is authorized for (the same check Run makes). A verified link limits that window to those stores. This is not a hard wall:
  the poster uses a SQL login, so whoever starts the poster by hand with the saved connection string can pick any store that login can read. Real enforcement would mean the poster
  fetching the package from the site after signing in, which is not built.
- **One window:** a second click passes its link to the open window (a pipe only the same Windows user can use). During a run the link is refused politely.
- **Registration:** per Windows user under `HKCU\Software\Classes\ddposter`, no administrator rights. The poster asks once on first start, has a **Register / Unregister DD Review links** button,
  fixes the registration itself if the program was moved, and `DDQuickBooksPoster.exe --register` / `--unregister` do it without a window.

### Setting it up
1. Make a key per site (use a different key id for staging and production):
   `dotnet run --project tools/DDReview.PosterKeyTool -- prod1 <folder>`. It writes `prod1.signing-key.private.txt` (never commit it) and prints the public key entry.
2. On the server, put the private key's text in the secret settings as `PosterLink:SigningKeyPkcs8` and the id as `PosterLink:KeyId`
   (see `deploy/appsettings.Production.example.json`), then delete the private key file. With both empty the button is not shown. One set and not the other stops the site at start.
3. Add the printed `{ "kid": ..., "publicKey": ... }` entry to `deploy/poster-trusted-keys.json`, commit it, and publish the poster again so the keys file goes into the zip.
4. On each PC: unzip, run the poster once, answer Yes to the links question (or press **Register DD Review links**).

## Not yet (on demand)
Vendor bills and credits (A/P), payroll, beginning balances, club minimum invoices, QuickBooks Online, Canadian tax beyond copying the old settings,
other accounting packages. `PostingPlan` is neutral (no QuickBooks types in it), so another package can be a second writer next to `QbxmlRequests`.

## Not checked against a real QuickBooks from here
The request layout is checked against the XML and log Access produced, and the engine against a fake that behaves like the logs show.
First real run: use a test company file, Preview, then Post, then Preview again (everything should say unchanged).
