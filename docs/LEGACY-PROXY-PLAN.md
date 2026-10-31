# Plan: put the old DDReview (WebForms, VS 2008) behind the new site

Status: **agreed in principle, nothing built yet.** Decided with the owner on 2026-10-31. The owner is checking which copy of the old source is the best one before work starts.

## Goal
Turn the old production server (VS 2008 era) off without losing the features that are not ported yet (Costs review, Options tab, Specials/Daily Discounts, the Dundas gauges and charts, anything else customers still use),
and stop exposing the old site's weaknesses to the internet. The new site becomes the only public front door; the old site runs on the new server (Windows Server 2025, monty.social) where nobody can reach it directly.

## Decisions so far
- **Reverse proxy (YARP) in `DDReview.Web`, not a frame.** A frame leaves the old site reachable by browsers. Same-origin proxying keeps cookies simple. A same-origin frame inside the new layout (header on top, old page below) is allowed for the look.
- **The old site is installed on the new server on `127.0.0.1:<port>` only**: its own app pool and low-privilege identity, no public binding, no DNS name, no certificate, firewall rule blocking the port from outside, no outbound internet (SQL Server only).
- **Same `DDReview-CentralMaster` as the new site** (the owner is fine with this; staging and production of the new site share it too). But the old site gets **its own SQL login**, least privilege (read-only where possible); client databases too.
- **Customer sign-in happens once, in the new site** (password or Google). The old login page is bypassed with a signed handoff once that works.
- The owner wants to keep an instance of the old site "for customer comfort"; it has an end date, ported page by page, and is removed when nothing is needed from it.

## Phases
1. **Install** the old site on the new server on loopback only. Needs .NET Framework 3.5 (Windows feature) or whatever it targets, possibly a 32-bit app pool for the Dundas gauge/chart libraries (the owner says those and the tab control installed on Server 2025). Check it works from the server itself.
2. **Proxy route** `/classic/{**catch-all}` (or a subdomain such as `classic.ddreview.com`, with the sign-in cookie shared across `.ddreview.com`, if the old pages use root-relative URLs that break under a prefix: read the source to decide). Requires a signed-in user. Interim: the old login page still appears (double login).
3. **Signed handoff** (removes the double login). The new site mints a short-lived ticket (user id, expiry about 60 s, HMAC-SHA256 with a shared secret kept in each side's config, not in git), and the old site accepts it only from loopback and only with a valid signature. On the old side this is **one new file** (C# 2.0-compatible, `System.Security.Cryptography.HMACSHA256`) plus a few lines in the existing login/session code. Write the same token format in the new site with unit tests, so both ends share test vectors.
4. **Lock down:** path allow-list in the proxy (pages, images, the gauge/chart handlers; block `trace.axd`, `elmah.axd`, and the like), strip internal headers, log every proxied request, least-privilege SQL logins, firewall rule, request size limits.
5. **Retire pages** as they are ported; remove the route when the last one goes.

## What this does and does not fix
It removes the old site's exposure to **anonymous** attackers. A signed-in customer can still abuse the old pages' string-built SQL; the answer to that is least-privilege logins, each customer having their own client database, and porting the pages.

## Working on the old code
Claude can edit the VS 2008 C# as text but **cannot compile it** (no VS 2008 / .NET Framework toolchain in the cloud workspace). Keep changes small and isolated, use only syntax VS 2008 accepts for the project's target framework,
and expect the owner to build and test in VS 2008. Never commit secrets, connection strings or customer data from the old source.

## To start (next chat)
1. Owner confirms which copy of the old source is best, and links the session to the computer.
2. Read `DDReviewMarauder\web.config`, `LoginPageClass.cs`, `BasePage.cs`, `Default.aspx(.cs)`: target framework, session/login handling, how URLs are built (relative vs root-relative), which handlers the gauges and charts use, which SQL logins it uses.
3. Decide path prefix vs subdomain; write the proxy route and the handoff token (new side first, with tests); then draft the minimal old-side change for the owner to build in VS 2008.
