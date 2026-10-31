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

## Not yet (on demand)
Vendor bills and credits (A/P), payroll, beginning balances, club minimum invoices, QuickBooks Online, Canadian tax beyond copying the old settings,
other accounting packages. `PostingPlan` is neutral (no QuickBooks types in it), so another package can be a second writer next to `QbxmlRequests`.

## Not checked against a real QuickBooks from here
The request layout is checked against the XML and log Access produced, and the engine against a fake that behaves like the logs show.
First real run: use a test company file, Preview, then Post, then Preview again (everything should say unchanged).
