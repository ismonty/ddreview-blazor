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
- Other export types, Payroll Status, Time Entries review, Manual / Additional Payroll, Consolidated Tip Outs (Data Entry/Review) and its ADP tip export.

## Who can use it
Payroll and Consolidated Payroll are optional features. They are off for everyone except administrators until an administrator turns them on for a user
(Account > Optional features, `/admin/features`). Administrators always have them. A user still needs the Payroll report authorization for their stores.
- Stored in `dbo.tblWebUserFeatures` (new script `sql/005_client_web_features.sql`, added by Admin > Database updater): a row means on, no row means off. If the table is not there yet, non-administrators have nothing.
- The Payroll page checks the switch when it opens and again on every Run and Export; the nav link only shows when it is on.
- The web login needs `select, insert, delete` on that table; script 004's grant procedure now lists it, and the updater shows the one grant line if it is missing.
- `WebFeature.ConsolidatedPayroll` exists and is switchable now; the Consolidated Payroll screen itself is not built yet and will use the same check.
