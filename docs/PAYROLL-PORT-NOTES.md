# Payroll port: working notes

New project `src/DDReview.Payroll` (tests in `tests/DDReview.Tests/PayrollTests.cs`), page `/payroll`.
Source: the Access presentation layer (`frmPayrollReview`, `mod Payroll Export` `ExportADPPayroll`, `clsEPIP`). The calculations stay in the existing SQL functions
(`fn_LaborPayrollReviewByDate3`, `fn_LaborPayrollExportByDate3`, `fn_LaborPayrollExportMealsByDate`), which the Access front end still uses, so nothing in the database changes.

## Ported
- Payroll Review: store, dates, Store / Employee / Week / Department grouping, grid, Totals row (the old `RedrawTotals`).
- Export Payroll File for ADP (`PayrollExportType = 1`). Other types say they are not ported yet. Files are downloaded (zipped when there is more than one); the old program wrote to a folder.
- After a successful download the next ADP batch number is stored in `tblGlobal.PayrollExportADPBatchID`, as the old export did.

## Deliberate differences from the Access code (check these against a real export)
1. Earnings slot 4: the old class counted slot-4 earnings under slot 3, so the Earnings 4 columns were never written. Slot 4 is now tracked and written correctly.
2. A follow-on line (a fifth memo, for example) now keeps its employee's file-name code and sort key; the old one lost both and sorted away from the employee.
3. A value too long for an import field (code more than 3 characters, file-name code more than 2, and so on) stops the export with the field named. The old in-memory recordset would have failed less clearly.
4. The ADP function works by payroll group, not by store. The export refuses to run if the group contains stores the user is not authorized for.
5. Totals "doubletime" is the DoubletimeHours column only, as on the old form's grid (the old form's separate SQL total added holiday overtime; the grid total, which the user saw, did not).

## Not done yet
- No comparison with a real ADP file from the Access program has been made. That is the first thing to do on the restored test databases.
- Other export types, Payroll Status, Time Entries review, Manual / Additional Payroll.

## Who can use it
Payroll and Consolidated Payroll are optional features. They are off for everyone except administrators until an administrator turns them on for a user
(Account > Optional features, `/admin/features`). Administrators always have them. A user still needs the Payroll report authorization for their stores.
- Stored in `dbo.tblWebUserFeatures` (new script `sql/019_client_web_features.sql`, added by Admin > Database updater): a row means on, no row means off. If the table is not there yet, non-administrators have nothing.
- The Payroll page checks the switch when it opens and again on every Run and Export; the nav link only shows when it is on.
- The web login needs `select, insert, delete` on that table; script 004's grant procedure now lists it, and the updater shows the one grant line if it is missing.
- `WebFeature.ConsolidatedPayroll` exists and is switchable now; the Consolidated Payroll pages use the same check.

## Consolidated payroll (page `/consolidated`, switch `ConsolidatedPayroll`)
Ported: the **ADP sales and tips file** (the Export form of Data Entry/Review) and its settings (tblGlobalSettings tags `ADPCoCode` ... `ADPUnpaidChargedTipsNegativeDeductionCode`; saved by administrators through `cs_spGlobalSettingsSet`).
It runs `cs_spConsolidatedTipOut3Crunch`, then reads `fn_ConsolidatedTipOutADPExport3` (payroll group 1, as the old form passed), and writes the CSV (header from the first row, memos rounded to whole dollars, deduction as is).
- The crunch procedure empties and refills one shared table (`tblConsolidatedTipOutTempTable`), so runs are queued per database inside the program. That does not protect against two web servers on one database, or against Access running at the same moment.
- The web login needs execute on `cs_spConsolidatedTipOut3Crunch` and `cs_spGlobalSettingsSet`; script 004's grant procedure now lists both (re-run it, or run the two grant lines).
- The ADP period dates and batch number are not advanced by this export (the old form did not either).

## Consolidated tip outs: Data Entry/Review (`/consolidated/tipouts`, `/consolidated/report`, `/consolidated/letters`)
Built from the Angus Barn schema script, whose objects override the first schema for everything consolidated.
- The screen runs `cs_spConsolidatedTipOut3Crunch2` for the one day, then reads `fn_ConsolidatedTipOut32(store, day, day, sourceFlag, staff, byReceipt, payrollGroup) where SourceLevel<99`, exactly as the form's Retrieve did. Display Mode bit 1 is "Show source levels", bit 16 is "Classify by individual receipt". A store whose `PayrollGroupNumber` is not above 0 shows nothing, as in Access.
- Totals are column sums; hovering the sales and tips totals shows the adjustment part and the Digital Dining part (`fn_ConsolidatedTipOutTotalAdjustments`, first row), like the old control tips.
- Clicking an employee drills to that person (the Access double-click opened a one-person form in source mode); Adjustments opens the day's adjustment lines for that batch with add, change and delete.
- Adjustments are checked more strictly than the Access datasheet: a person on at least one side, a department group with each person, both people and groups in the batch's store, percentages 0 to 100, source level 1 to 98. The crunch silently drops a line whose person or group does not exist, so Access let mistakes hide.
- The print report (`rptConsolidatedTipOuts`) uses payroll group 0, no source detail, and rows with `NonZeroBit<>0`. The compliance letters crunch the year-to-date start through the period end with `cs_spConsolidatedTipOut3CrunchComplianceLetters2`, read `fn_ConsolidatedTipOutDateRangeWithYearToDate32(..., 1) where NonZeroBit<>0`, and show the note from `tblGlobal.ConsolidatedTipReportingComplianceLetterNote`. The store total row (source level 99) is shown separately.
- Layouts are a reasonable guess: the Access form and report designs were not in the export. Column choice and the letter wording are easy to change once you see them.
- Each of the three work tables (`tblConsolidatedTipOutTempTable`, `...TempTable2`, `...ComplianceLetters2`) has its own queue inside the program. Same limits as above.
- The period start, period end and year-to-date dates were remembered in the Windows registry by Access; the web does not remember them.
- **Check this with the client:** the day screen uses `Crunch2` / `fn_ConsolidatedTipOut32`, but the ADP sales-and-tips export still uses the older `Crunch` / `fn_ConsolidatedTipOutADPExport3` (as the Access export form does). The newer procedure splits the tip out by cash/credit and service-charge sales and takes 1.75% off discretionary tips on service charges; the older one does not, and applies the dated second tip-out rate only to the sales portion. The two can disagree for dates after a rate change.
- The web login needs the grants in script 004's list (execute on both crunch procedures, select on the functions, select/insert/update/delete on the adjustments table). Re-run the grant procedure.

## Exports page (custom reports)

- `/reports/custom` (nav "Exports", same permission and feature as Payroll) lists `dbo.fn_CustomReports_Index()` from the client database, runs the chosen report function with the authorized store list and the two dates, previews it, and downloads .xlsx or .csv. A function is only run if it is in that index, is a plain `dbo` name, and takes exactly `@StoreIDList, @StartingDate, @EndingDate`. A leading `ItemID` column is dropped from the export. Script 004 grants select on the index and on each listed function.
- `fn_CustomReport_TipsExport` ignores `@StoreIDList` (store 13 is hard-coded). The Exports page passes the list correctly, so that report will only be right once the function is fixed; the new-format export will honor the list.

## Open items for later cleanup (Angus Barn, 2025 tip law)

- **Vicky's column-K fees.** In her example spreadsheet, some rows show a red fee in column K that Bob's net does not subtract (Hester, Hopkins, Kennedy, King, Koerselman, McPeek, Olvera-Martinez, Wright, Banq 1/2, Wine Cellar and others). Not resolved; left as is.
- **Partly gift-card-paid service charges.** Some rows show an effective fee of about 0.7% or 1.5%, which suggests the 1.75% applies only to the card-paid part. Checked against real April 2026 checks: the service charge is split across the receipts, and the fee is charged per receipt on the card-paid part only (floor(1.75*AmountCharge+0.5)/100 for receipt class 2), so a split check is already right and the lower effective rate is expected.
- **Fee rounding.** Per-receipt half-up rounding makes the overall rate about 1.755%.
- **AB HRS, DCL and AD2 codes** in Vicky's Paycor import: origin unknown (probably entered by hand).
- Rates (3.2%, 1.75%) and excluded departments or pay methods should become per-client settings.
- **Join rule for new SQL.** Receipt types (and most other tables) are per `StoreID`, and a past store switch means the same `ReceiptTypeID` exists under two stores. Any join between tables that both have `StoreID` must include it, or rows double. Existing objects were only spot-checked for this; none in the consolidated or payroll path were found to be affected.

## New-format tips (script 020, for the "new company" database)

- `dbo.fn_TipsByEmployee(@StoreIDList, @Start, @End)`: one row per store, employee and department group. It reads the receipts directly (no work table, so nothing stale and no crunch step) and returns the pay lines **CTP** (cash tips), **CCT** (credit card tips less the tip out on cash and card sales, plus the voluntary tip on top of a service charge, tip net of the card fee) and **SVC** (service charge gross, less the 1.75% card fee on the card-paid part, less the tip out on service charge sales). It also returns the pieces separately (`ServiceChargeGross`, `ServiceChargeFee`, `VoluntaryTipOnServiceCharge`, the two tip outs) for the two-part report and letters.
- `dbo.fn_CustomReport_TipsExportNew`: the Paycor import lines built from it, one CTP/CCT/SVC line per employee and payroll department. It honors the store list. Add it to that database's `fn_CustomReports_Index` to get it on the Exports page.
- The consolidated screen's manual moves (adjustments table) are applied the same way. Old objects are untouched.
- **Written without a live database to run against.** First check: pick a pay period, compare `fn_TipsByEmployee` with the consolidated screen and with Vicky's spreadsheet figures (e.g. a server's SC 226.80, fee 3.97, tip out 36.29, SVC 186.54).
- Known simplifications: the 1.75 is a constant in the script (becomes a per-client setting); a service charge check is any check whose receipts carry a charge (as Crunch2); the voluntary tip uses the fee already stored on the receipt rather than recomputing 1.75% as Crunch2 does for its discretionary column.
- `fn_CustomReport_TipsExport` (old) joins `tblStaffDepartments` on StoreID alone, which repeats each employee's rows once per department of the store and so can multiply the cash tip sums. The new function does not do this.

## Who is right when they disagree

Vicky St Clair (the client's payroll authority) is the authority on the tip rules and figures. The existing Access objects (Crunch2 and the consolidated functions) were built by the owner and are not necessarily correct. Where the old objects and Vicky's spreadsheets or letters differ, follow Vicky, and write the difference down here rather than copying the old behavior. The column-K fee mismatch above is the first thing to settle against her figures: her red fees may be the correct ones, with the old net being the part that is wrong. Script 020 follows Vicky's stated rules; where it borrows Crunch2 (what counts as a service charge check, per-day tip out rounding) that is only because nothing from her contradicts it yet.

## Tip report and letters (new format)

- `/payroll/tips` (same permission and ConsolidatedPayroll feature as the consolidated screens): the two-part report laid out like Vicky's 8/20/25 sample. Page 1 is "Voluntary tips tip report" (cash and card sales, tip out %, tip out portion, credit card tips, less tip out = CCT, cash tips = CTP); page 2 is "Compulsory service charge report" (SC sales, tip out, service charges, card fee, less tip out and fee = SVC, voluntary tips on SC = CCT). Both pages carry the employee names and are grouped by department group; each prints on its own page.
- `/payroll/tips/letters`: one letter per employee, with the voluntary block and the compulsory service charge block, this period and year to date. Year to date is just the same function over a longer range, so the "new company" database needs no year-to-date table.
- Both read `dbo.fn_TipsByEmployee` (script 020) for one store at a time. The old consolidated screens and letters are unchanged.
- Differences from her sample to settle with her: the sample's "SC less tip out" column has no card fee (August); ours subtracts the 1.75% fee as her later spreadsheets do. The tip out % shown is the department group's rate at that time, not worked out from the amounts.
