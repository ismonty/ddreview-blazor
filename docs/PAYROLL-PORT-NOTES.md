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
