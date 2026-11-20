# Mirroring a client database to a second Express server (notes for a future mod)

Not built. Written from the plan the owner described: the Angus Barn database is restored as a copy on a newer server, schema changes follow by passing the numbered scripts on (DDReviewService reads scripts and can forward them), and the **user-entered** data of the consolidated payroll still has to be kept the same in both copies by software, because both servers are SQL Server Express.

## What scripts already cover

- Objects: the numbered client scripts (002, 004, 007, 010-020) are idempotent and create or alter objects only. Forwarding them in order is safe, and the updater's Present/Usable checks tell whether a script has landed. Script 020's functions only read.
- Not covered by scripts: any row that a person types or that a program writes while it runs.

## Data written by people or by the programs (candidates to mirror)

| Table or column | Written by | Notes |
| --- | --- | --- |
| `tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments` | consolidated screen (web and Access): insert, update, delete | The main one. Has an identity key. Deletes must be mirrored. |
| `tblGlobal.PayrollExportADPBatchID` | ADP export (web and Access) | A counter. If both servers can export, they will hand out the same number twice. Pick one server that exports. |
| `tblGlobal.ConsolidatedTipReportingComplianceLetterNote` and other `tblGlobal` settings | owner / Access | One row, edited rarely. |
| `tblStaffDepartmentGroups` (tip out percentages, second percentage and its effective day), `tblStaff` (PayrollID), `tblStaffDepartments` | Access screens / poster | Change what the tip math produces. Must match or the two servers disagree. |
| `tblReceiptTypes` (ReceiptClass, GiftCard, ...) | receipt type editor (web and Access) | The card fee depends on ReceiptClass. Keyed by StoreID plus ReceiptTypeID. |
| `tblWebUserFeatures`, `tblWebUserSecurity`, `tblWebUserSecurityStores` | web admin | Probably should differ per server (different people may log in). Decide per table. |
| Receipt, batch and time entry tables | the poster / nightly import | Larger than everything above. Decide first where they enter (see "One writer"). |

Never mirror: `tblConsolidatedTipOutTempTable`, `...TempTable2`, `...ComplianceLetters2` (crunch work tables, rebuilt on every run, and only one crunch at a time is safe); grants and logins (they are per server).

## Express limits that shape the design (SQL Server 2012)

- No SQL Server Agent, so nothing scheduled inside the server. The service app has to do the polling.
- Express can subscribe to replication but cannot publish; no log shipping, no database mirroring, no Always On. Change data capture is not in Express. **Change Tracking is** (SQL 2008 and later, every edition) and needs a primary key on each tracked table.
- 10 GB data limit per database (2012). A second copy has the same limit; keep an eye on receipt detail growth. Newer servers may allow more, but a copy that outgrows the old one cannot be restored back.
- Compatibility level 110: no `STRING_SPLIT`, no `CREATE OR ALTER`, no `TRY_CAST` surprises beyond what 2012 has. Scripts written for the new server must still run on the old one until it is retired.

## Approaches, simplest first

1. **One writer per table (recommended start).** Decide which copy people type into, and push to the other only. No conflicts to resolve. Consolidated entry happens in one place (the new web app, on one server); the other copy is read-only for those tables.
2. **Change Tracking plus a pusher in the service.** Enable change tracking on the few tables above (a numbered script, per database). The service remembers the last version it applied, asks `CHANGETABLE(CHANGES ...)` for the keys that changed, reads the current rows, and applies them to the other server with an upsert by key; keys in the changes with no current row are deleted. Handles deletes, survives the service being down, and needs no triggers.
3. **Outbox triggers.** Triggers on the tables write the key and the action to an outbox table; the service drains it. Works without change tracking and keeps the order of changes, but a trigger is a change to a table Access also uses.
4. **Dual write in the web app.** Not recommended: one server down means a half-written change, and Access writes would bypass it.

Whatever is chosen:
- **Identity keys.** The adjustments table's identity (and any other) must keep the same value on both sides: `SET IDENTITY_INSERT ... ON` when applying, or key on the natural key (BatchID, StoreID, staff, department group, source level). Two servers handing out identities independently will collide.
- **One writer for batches.** BatchID and receipt identities come from the posting process. If both servers receive the poster's data independently they will not match. Either feed one and copy, or make the second feed apply the same rows with the same IDs.
- **Order and idempotence.** Apply changes in version order inside a transaction per batch; an upsert-by-key applied twice must give the same result. Record the last applied version in a small table on the target.
- **Conflicts.** If both sides can be edited, last writer wins is wrong for payroll. Prefer approach 1; if two writers are unavoidable, keep a conflict list for a person to resolve.
- **Script and data together.** A script that adds a column to a mirrored table must reach the target before the pusher sends that column. The service should hold data pushes for a table until the target's script level equals the source's.
- **Initial copy.** Backup and restore gives identical data and identical script level; record the starting change tracking version at the moment of the backup (take the backup, then read `CHANGE_TRACKING_CURRENT_VERSION()` in the same quiet window), or stop writes briefly.
- **Checking.** A nightly comparison per mirrored table: row count and a checksum by store and date range (`CHECKSUM_AGG(BINARY_CHECKSUM(*))`), and the ADP batch counter. Differences go to a log page the owner can read.
- **Security.** The target connection string and credentials live with the service, not in the web app's settings page; the pusher needs insert, update, delete on the mirrored tables and nothing else.
- **Failure behavior.** The pusher must never block data entry. If the other server is down, changes queue up (change tracking keeps them) and a retention period longer than any expected outage is needed; if retention is exceeded, the table must be re-copied in full.

## For the web app side of a future mod

- A read-only mode per database (a flag in the central master record) that makes the consolidated entry pages and ADP export display the data but not save, so the copy cannot be edited by accident.
- Show on the Payroll pages which server the user is connected to and when the last mirror run finished.
