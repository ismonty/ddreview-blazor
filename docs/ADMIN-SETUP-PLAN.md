# Plan: Setup (Admin) pages that replace the DDAdminCS "G/L Setup" and "A/R Setup" button columns

Status: plan only, nothing built. Written 2026-11-01 from `DDAdminCS-ExportedCode.zip` and the two schema dumps in `C:\EOD\DDReviewBlazorPort`.

## 1. What the Access side actually is

In `frmMain` the tab control `tabMain` has two pages with a column of buttons down the right-hand side:

* **G/L Setup** (13 buttons) and **A/R Setup** (11 buttons). (The page called "G/L and A/R" is the reports/export page and is not part of this.)

Each button just opens an Access form bound straight to a table. There is almost no logic: the forms are grids over one store's rows, with store drop-down, account-class drop-down and a few double-click helpers. Every form writes directly into the client database tables, so the Access app and the new pages can run side by side against the same rows.

### G/L Setup column

| Button | Opens | Table(s) it updates | Notes |
|---|---|---|---|
| Tax/Royalty/Fee Types | frmAccountsTaxTypes | `tblTaxTypes` (TaxTypeID, TaxType, RoyaltyFee) | Not store-scoped. |
| Tax Rates/Accts | frmAccountsTaxRates | `tblTaxRates` where RoyaltyFee=0 | Per store. Falls back through older column sets (UseTaxApplies, UseTaxPercentage, Enabled may be missing). |
| Royalty/Fee Rates/Accts | frmAccountsRoyaltyAndFeeRates | `tblTaxRates` where RoyaltyFee=1 | Extra columns: DebitQuickBooksType, CreditQuickBooksType, RoyaltyPeriod*, ProfitCenterAccountClassID. |
| Sales Accounts | frmGLSetupSalesAccounts | `tblAccountsCredit` | The GL credit accounts for sales; 33 columns (redirects, adjustment accounts, Suspense, Exclude, UseTaxApplies, DefaultQuickBooksType, POSWindowID if present). |
| Sales Account Mapping | frmAccountsSalesMapping | `tblAccountsSalesMapping` | POS object (main type, profit center, sale type, PLU, discount, POS window, special, tax exempt) -> a `tblAccountsCredit` row, in Priority order. Cascading drop-downs (`fn_MenuItemList`, sale types filtered by main type). Tri-state Special / TaxExempt (true / false / null). |
| Receipt Account Mapping | frmAccountsReceipts | `tblReceiptTypes` (44 columns) | POS receipt type -> debit/contra/discount accounts, class, AR account, QB type. Double-click fills a default account name and DistReference. |
| Receipt Adjustments | frmSetupReceiptTypeAdjustments | `tblReceiptTypeAdjustments` | Factor + debit/credit accounts + the Include* checkboxes (IncludeAll clears the others, any other clears IncludeAll). |
| Paid Outs | frmSetupPayoutTypes | `tblPayouts` | Adds column TipsShortfall on open if missing (we will not do that at page open, see section 5). |
| Gift Cards | frmSetupGiftCardTypes | `tblGiftCardTypes` | Double-click copies the type name into DistReference. |
| Account Classes | frmSetupAccountsClasses | `tblAccountsClasses` | Inter-class accounts; OtherAccountClassID drop-down excludes itself. |
| Store Setup | frmStores (GeneralLedgerOnly) | `tblStores`, the "General Ledger" page only (gift certificate sold/bonus, tips withheld, charged tip fee, pay-in, taxes over/short, AR offset accounts, CheckbookID; each with DistReference and AccountClassID) | |
| Global Settings | frmSetupGlobal (GeneralLedgerOnly) | `tblGlobal`, the "G/L and A/R" page only (GeneralLedgerPostDateNumberOfDaysLater, plus whatever else that page shows; to confirm) | One row table. |
| Check QuickBooks | message box only | none | A stub. Skip. |

### A/R Setup column

| Button | Opens | Table(s) it updates | Notes |
|---|---|---|---|
| A/R Accounts | frmSetupAccountsReceivableGLDirectPostings | `tblAccountsReceivableAccounts` (DebitAcct, DistReference, AccountClassID, Inactive, ParentAccountID, MinimumsApply) | Parent change is checked with `dbo.fn_TraverseAccountCore` (>= 1000 means a circular reference). "Inbreeding check" button opens a parent-conflict list. |
| A/R Account Types | frmSetupAccountsReceivableARAccountTypes | `tblAccountsReceivableAccountTypes` | Receipt type drop-down built from `tblReceiptTypes` (ReceiptTypeID < 100, plus "None"). |
| A/R Parent Accounts | frmSetupAccountsReceivableParentAccounts | `tblAccountsReceivableAccounts` (ParentAccountID, AccountNameQB) | Same table as above; sorting options; display name depends on `tblGlobal.ARAccountIdentifier` (0 number, 1 name, 2 number+name, 3 name+number). |
| Comment Substitution | frmAccountsReceivableCommentCodeSubstitution | `tblAccountsReceivableCommentCodeSubstitutionTable` (CommentCode, CommentOutput) | Tiny. |
| Other A/R Descriptions | frmSetupAccountsReceivableMenuItemDescriptions | `tblMenuItems.AccountsReceivableNote` (POS item rows, sale types of main type 3) | Edits a POS table; only that one column. |
| Global Settings | frmSetupGlobal | same as above | |
| Credit Card Assignment | frmAccountsReceivableCreditCardAssignment | `tblAccountsReceivableAccountsReceiptCustomerIdentifiers` (AssignedStoreID, AssignedCustomerID) | Filter box; usage counts for the club-minimum period. |
| Import Beginning Balances | frmSetupAccountsReceivableImportBeginningBalancesAndInformation | needs a look (uses `fn_AccountsReceivableAccountsBeginningBalances`) | No click handler in the export; ask before porting. |
| Strip Leading Account Numbers in QuickBooks, Extract QB Customers, Post Club Minimum Invoices | QuickBooks code | read/write QuickBooks (Extract QB Customers fills `tblAccountsReceivableAccountNames`) | **Not web work.** They need a running QuickBooks Desktop and belong in the WinForms poster. |

## 2. Scope split

* **Phase set A (web):** everything in the two tables above that only edits SQL tables: about 12 tables behind 21 buttons.
* **Not in the web app:** Check QuickBooks, Strip Leading Account Numbers, Extract QB Customers, Post Club Minimum Invoices. Import Beginning Balances: decide later.

## 3. Who may use it (three conditions, all enforced on the server)

1. **Administrative user.** The client database's `tblWebUserSecurity.Administrator = 1` (already loaded into `UserContext.Access.Administrator`, and what the Database updater uses).
2. **Google-signed-in session only.** Today a Google sign-in produces exactly the same cookie as a password sign-in (`FinishSignInAsync`), so nothing tells them apart. Change: add an `auth_method=google` claim when the sign-in came from `/account/google/done`, and have an authorization policy `SetupAdmin` require it. Password sessions get a "sign in with Google to use setup" page. Because the cookie lives for a long time, also require a *recent* Google check for this area: a "Verify with Google" step that challenges with `prompt=login` (ASP.NET `GoogleChallengeProperties.PromptKey`) and stamps an `admin_verified_utc` claim valid for 30 minutes. Every setup page and every save checks it (not only the menu link).
3. **Explicitly granted.** A new client-database table lists who may use which setup area, and only Administrators can edit it.

   Proposed rule (please confirm): *Administrators always have access (so nobody can lock themselves out); an Administrator can additionally grant specific areas to other client logins; only Administrators can change grants.* The alternative reading, "an Administrator must also hold an explicit grant", is a one-line change in the policy.

Areas that can be granted separately: `GlSetup`, `ArSetup`, `StoreAndGlobal` (the Store Setup and Global Settings pages), `Grants` (administrators only, not grantable).

Every save goes through one server-side check of all three conditions plus the area; a page being visible is never the check.

## 4. New database objects (new numbered scripts, safe to re-run, SQL Server 2012+)

* `sql/007_client_admin_access.sql` (client DB): `dbo.tblDDReviewAdminAccess` (ClientWebUserID, Area, GrantedByWebUserID, GrantedUtc, RevokedUtc null) and `dbo.tblDDReviewAdminAudit` (when, who, central WebUserId, Google email, table, key, action, old/new values as JSON, client IP).
* Register 007 in `ClientScripts.cs` (the Database updater then adds it) and add the grants to `dbo.usp_DDReviewGrantAccess`: select/insert/update/delete on each setup table above and on the two new tables. That is one place to review and you launch it yourself, as with today's pattern. Note this is a wider permission than the reports need; the Database updater page should say so.
* No change to any existing object. The GL functions (`fn_CashAndSalesGLandARMultiStoreSelect*`) read these tables, so a bad edit changes the next export; the audit table is the safety net, and the pages show "last changed by" on each row.

## 5. How the pages are built

* **Metadata-driven grid, not 21 hand-made pages.** `DDReview.Core/Admin/SetupTables.cs` declares each table once: table name, key column, whether it is store-scoped, ordering, and per column: name, label, type, max length, required, lookup (account classes, QuickBooks types, receipt types, sale types...), read-only, tri-state. One grid component (`SetupGrid.razor`) renders any of them with add / edit / delete / save row. Identifiers come only from these declarations (whitelist); every value is a parameter. (Access built SQL by string concatenation.)
* **Hand-built pages only where the old form has real logic:** Sales Account Mapping (cascading lookups, priority, tri-state), A/R Accounts and Parents (circular-reference check via `fn_TraverseAccountCore`, display-name mode), Receipt Adjustments (Include* mutual exclusion), Credit Card Assignment (filter plus counts).
* **Business rules stay in SQL; C# stays thin.** Keep calling `fn_MenuItemList`, `fn_TraverseAccountCore`, `fn_ParsePairSetStringAdd*`.
* **Older schemas.** The Access forms try several column lists and fall back. The service will read `sys.columns` once per client database and only show/edit columns that exist (UseTaxApplies, Enabled, POSWindowID, DefaultQuickBooksType, TipsShortfall, ...). It will not `ALTER TABLE` at page open the way the Paid Outs form does; adding a missing column becomes a numbered script in the Database updater if you want it.
* **Behaviours to carry over:** account-name text has the accented e replaced with a plain "e" on save (QuickBooks name matching); double-click defaults (receipt account/DistReference, payout account, gift card DistReference); Priority/Sort ordering; active-stores-only store drop-down (`tblStores.Active > 0`); friendly store names via `StoreReportName` (shown), keeping `StoreName` (the hard-lock name) untouched and read-only.
* **Concurrency.** Access forms were last-write-wins. Updates will carry the original values of the edited columns in the WHERE clause and say "changed by someone else, reload" instead of overwriting. Deletes confirm, and a row referenced elsewhere (tax rates have FKs to stores and tax types) shows the SQL error in plain words.
* **A zero-row result never crashes a page**; store pickers with one active store pre-select it (as the Access forms do).
* **Look:** Twin Ports style; a Setup entry in the nav only when the policy passes, with two sections (G/L Setup, A/R Setup) laid out like the old button columns so end users find things where they used to.

## 6. Build order (small PRs, each one something you can try on staging, often from a phone)

0. Access foundation: sql/007, `auth_method` claim, `SetupAdmin` policy, Google step-up, `/admin/setup` landing, grants page, audit writer, tests. Nothing editable yet.
1. Account Classes, Tax/Royalty/Fee Types, Tax Rates, Royalty/Fee Rates. (First use of the generic grid.)
2. Sales Accounts and Sales Account Mapping.
3. Receipt Account Mapping, Receipt Adjustments, Paid Outs, Gift Cards.
4. Store Setup (GL fields) and Global Settings (GL fields).
5. A/R Account Types, A/R Accounts and Parents, Comment Substitution, Other A/R Descriptions, Credit Card Assignment.
6. (Later) Import Beginning Balances if wanted.

Tests per slice: policy tests (password session denied, Google but not Administrator/not granted denied, granted non-admin allowed only in their area, expired verification denied), SQL-shape tests for each table definition (columns exist in the schema dump), concurrency conflict test, and the circular-parent test against a fake.

## 7. Decisions (owner, 2026-11-01)

1. Administrators always have access; an administrator can grant areas to other logins; only administrators change grants. (Section 3 as proposed.)
2. One person per login. Google linking is one Google account per login, so the Setup rules assume logins are not shared.
3. Administrators see all active stores in Setup.
4. Import Beginning Balances is dropped for now (and with it `fn_AccountsReceivableAccountsBeginningBalances`).
5. The missing form layout files are not needed; labels and order come from the schema and the Access code.

## 8. Progress

* Slice 0 (access foundation): `sql/007`, grant procedure lines, `auth_method` and `admin_verified_utc` claims, `SetupPolicy`, `/admin/verify`, `/admin/setup`, `/admin/setup/access`, audit writer. Done on branch `admin-setup-0-access`.
  * A Google sign-in counts as Google vouching for the person at that moment. After 30 minutes the Setup pages ask again (`prompt=login`), and a password session can also step up this way.
  * The grants cache is 30 seconds and is cleared when an administrator changes a grant; anything that saves reads the grants fresh.
