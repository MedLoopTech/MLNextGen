# MedLoop NextGen

A from-scratch parallel implementation proving out the "next version" stack proposed for `PharmacyWebapp`: ASP.NET Core 8, PostgreSQL, and real authentication — instead of Firestore and the legacy app's ad hoc session-based auth.

This is **not** a fork of `PharmacyWebapp` and contains none of its code. It's a clean skeleton meant to be extended module by module, following the incremental migration plan written up for the legacy app.

## What's in this skeleton
- ASP.NET Core 8 Web API project.
- PostgreSQL via EF Core + Npgsql, with migrations applied automatically on startup in Development.
- Real authentication via ASP.NET Core Identity (`AddIdentityApiEndpoints` — built-in `/register`, `/login`, `/refresh` endpoints), with role support wired up (`AddRoles<IdentityRole>()`).
- `Dockerfile` that runs as a non-root user out of the box.
- `docker-compose.yml` for local development against a real Postgres instance — no cloud account needed to run this locally.

### Entities/modules implemented so far
- `Pharmacy`, `ApplicationUser` (FK'd to a pharmacy).
- `Branch`, `Warehouse` — both scoped to a pharmacy with real foreign keys (no denormalized name copies the way the legacy Firestore schema had `pharmacyName`/`branchName` duplicated onto every child record).
- `Category` — simple open-read marketplace taxonomy, admin-write.
- `Product` — the core listing entity. Consolidates the legacy schema's four overlapping status fields (`status`, `productStatus`, `isApproved`, `isRejected`, `isDisposed`) into one `ProductListingStatus` enum, and replaces the legacy's string-typed `expiryDate` (parsed ad hoc with `DateTime.TryParse` at every call site) with a real `DateOnly` column. `AvailableQuantity` (`Quantity - LockQuantity`) is a computed property, not a value mutated independently from multiple call sites.
- `GET /api/products/marketplace` — the MedLoop Connect-style browse endpoint, filtered and paged entirely at the database query level. The legacy equivalent (`MedLoopConnectController.getAllProducts`) fetched a page, then issued one extra Firestore read *per product on the page* just to compute locked quantity — that N+1 pattern doesn't exist here because `AvailableQuantity` needs no follow-up query.
- Ownership checks on every write endpoint that touches tenant data (`BranchesController`, `WarehousesController`, `ProductsController`): a pharmacy/branch/warehouse ID is always derived from the authenticated caller's own account (via `UserManager<ApplicationUser>`), never taken from the request body, and cross-tenant writes return `403`. This directly closes the gap found in the legacy app's `BidApprovalController`/`OfferStatusController`, where approving or rejecting a bid, or finalizing payment, never checked that the caller actually owned the resource being acted on.
- Deletes are soft (`IsActive = false` via `DELETE`, which really does an update) rather than hard deletes, and delete endpoints are real `[HttpDelete]` actions requiring auth — unlike the legacy app, where ~30 delete actions were exposed as unauthenticated `[HttpGet]` endpoints.

### B2B bidding (MedLoop Connect equivalent)
- `Bid` (replaces the legacy `OfferNegotiationModel`/`b2bOffers`), with `BidsController`:
  - `POST /api/bids` — place a bid. `BuyerPharmacyId`/`BuyerUserId` are always derived from the authenticated caller, never from the request body — the legacy `addNegotitation` action never set the equivalent `createdById` field at all, which silently broke the buyer's own "My Offers" screen and every approve/reject notification for the lifetime of that code.
  - The "already bid" check is scoped to `(ProductId, BuyerPharmacyId)` — the legacy check looked at pending bids from *any* buyer on the product, so one pending bid blocked every other pharmacy from bidding on the same listing at all.
  - Offer quantity is validated server-side against `Product.AvailableQuantity` — the legacy version only validated this in client-side JS.
  - `PUT /api/bids/{id}/approve` / `/reject` — both check that the caller's pharmacy actually owns the product being decided on (`Forbid()` otherwise). The legacy `BidApprovalController.approvenrejectRequest` had no such check at all — any authenticated caller could approve or reject any bid on any pharmacy's listing, including their own bid.
  - Approving a bid updates `Product.LockQuantity` under Postgres's `xmin` optimistic-concurrency token (`UseXminAsConcurrencyToken()` in `AppDbContext`), so two approvals racing to lock the same stock get a `409 Conflict` instead of silently over-locking — the legacy code did a plain read-then-write with no protection against exactly that race.
  - `POST /api/bids/{id}/complete-payment` returns `501 Not Implemented` on purpose. The legacy equivalent (`OfferStatusController.ProcessBidPayments`) hardcoded `bool overallPaymentSuccess = true;` with no real gateway call — every checkout "succeeded" and the order was marked Paid & Closed without anyone actually being charged. This skeleton would rather be honestly unfinished than repeat that.

## What's deliberately NOT here yet
Orders (post-payment fulfillment), the real payment gateway integration, PDF generation, email, scheduled jobs, the POS module, and the long-tail collections (notifications, feedback, rewards, promo codes, appointments, disposer/technician/distributor workflows). Those get added incrementally per the phased migration plan.

## Running locally
```bash
docker compose up --build
```
The API comes up on `http://localhost:8080`, with Swagger UI at `/swagger` in Development. Migrations run automatically against the `db` container on startup.

First time only — create the initial EF Core migration before the first run:
```bash
dotnet tool install --global dotnet-ef   # if not already installed
dotnet ef migrations add InitialCreate
```

## Running against a real (cloud) Postgres instead of the local container
Set the `ConnectionStrings__Default` environment variable (or an untracked `appsettings.Production.json`) to a real connection string — e.g. a Neon.tech database — and skip `docker-compose.yml`'s `db` service.

## Roadmap (maps to the phased migration plan)
1. **This skeleton** — auth + one entity, proves the stack end-to-end. *(done)*
2. Reference data — pharmacies (started here), branches, warehouses, products, categories, plans.
3. Transactional core — orders, B2B offers/bids, settlements. This is also where the MedLoop Connect flow bugs identified in the legacy app's audit (buyer identity never recorded on bid creation, no ownership checks on approve/reject, the payment step being a hardcoded stub) get fixed from scratch instead of ported over.
4. Long-tail modules — notifications, feedback, rewards, promo codes, appointments, disposer/technician/distributor workflows.
5. File storage, PDF generation, email, payment gateway integration, production hosting/deployment.

## Notes for whoever picks this up
- No real secrets are committed anywhere in this repo. The Postgres password in `docker-compose.yml` / `appsettings.Development.json` is a local-only dev default, not a production credential.
- `appsettings.Production.json` is gitignored — production config/secrets should go through environment variables or a secret manager, not a committed file.
- This was scaffolded based on a code audit and stack-migration plan for `PharmacyWebapp` (private repo, same org) — see that repo's audit docs for the full list of findings this rewrite addresses.
