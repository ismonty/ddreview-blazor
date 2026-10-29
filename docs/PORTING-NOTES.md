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
