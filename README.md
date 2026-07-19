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
  - The "already bid" check is scoped to `(ProductId, BuyerPharmacyId)` — the legacy check looked at open bids from *any* buyer on the product, so one open bid blocked every other pharmacy from bidding on the same listing at all.
  - Offer quantity is validated server-side against `Product.AvailableQuantity` — the legacy version only validated this in client-side JS.
  - Accepting a bid updates `Product.LockQuantity` under Postgres's `xmin` optimistic-concurrency token (`UseXminAsConcurrencyToken()` in `AppDbContext`), so two acceptances racing to lock the same stock get a `409 Conflict` instead of silently over-locking — the legacy code did a plain read-then-write with no protection against exactly that race.

#### Counter-offers / negotiation
A bid isn't just accept-or-reject — either side can propose new terms back, ping-ponging until one side accepts, either side rejects, or the buyer withdraws. `Bid.Status` tracks whose turn it is (`Pending`/`CounteredByBuyer` → seller's turn; `CounteredBySeller` → buyer's turn), and `Bid.OfferQuantity`/`OfferPricePerUnit` always reflect the current terms on the table — whoever accepts is agreeing to exactly those values, never anything sent in the accept request (it has no body). Every round, including the original offer, is recorded in `BidNegotiationRound` as an append-only audit trail (`GET /api/bids/{id}/history`) — the legacy app's UI hinted at a `"CounteredBySupplier"` status badge but never actually had backend support or any history for it.
  - `PUT /api/bids/{id}/accept` — accepts the current terms, locks stock, moves to `Approved`. Turn-aware: only whichever side it's currently waiting on can call this (`Forbid()` otherwise) — the legacy `BidApprovalController.approvenrejectRequest` had no ownership check at all, so any authenticated caller could approve or reject any bid on any pharmacy's listing, including their own bid.
  - `PUT /api/bids/{id}/reject` — same turn-awareness, requires a reason.
  - `PUT /api/bids/{id}/counter` — propose new quantity/price/message; flips whose turn it is and appends a `BidNegotiationRound`.
  - `POST /api/bids/{id}/cancel` — the buyer withdraws a still-open negotiation outright, in either direction.
  - Paying for an `Approved` bid is `POST /api/orders/checkout`, not a bid endpoint — see below. The legacy equivalent (`OfferStatusController.ProcessBidPayments`) hardcoded `bool overallPaymentSuccess = true;` with no real gateway call — every checkout "succeeded" and the order was marked Paid & Closed without anyone actually being charged. This skeleton's checkout would rather be honestly unimplemented (`MockPaymentGateway`, clearly named) than repeat that.

### Orders and payment
- `Order` + `OrdersController`, with `IPaymentGateway` as a swappable abstraction (`MockPaymentGateway` for now — clearly named, always "succeeds" with a fake reference, registered in `Program.cs`; a real gateway is a one-line DI swap, not a rewrite).
- `POST /api/orders/checkout` (body: just `{ bidId }`) is the fix for the legacy app's most severe payment bug: `PaymentController.ProcessCheckout` took `amount` as a plain query-string parameter and charged whatever the client sent, with no server-side recomputation — any buyer could edit the URL and pay $0.01 for a real order. Here, the amount is *always* recomputed from the bid's own stored quantity/price plus the server-configured `Marketplace:PlatformFeeRate` (`appsettings.json`); the request body has no amount field at all.
- Checkout is wrapped in a DB transaction: the gateway is charged first, then stock is decremented and the `Order` is created atomically — if either fails, the whole thing rolls back. A unique index on `Order.BidId` makes it impossible to create two paid orders for the same bid even under a race, on top of the app-level check.
- Known, documented gap (see the comment on `OrdersController.Checkout`): if the gateway charge succeeds but the DB commit fails, there's currently no automatic refund/reconciliation. That's an explicit TODO to solve *before* swapping in a real (non-mock) gateway — not something to discover in production.

### Order fulfillment and disputes
- `PUT /api/orders/{id}/fulfill` — the selling pharmacy confirms shipment; only valid from `Paid`, ownership-checked (only that order's seller can call it).
- `PUT /api/orders/{id}/dispute` — either the buyer or the seller can raise a dispute with a reason, valid from `Paid` or `Fulfilled`. Replaces the legacy `B2BOrderModel`'s free-floating `BuyerComments`/`SellerDisputeComment` fields — which anyone could set with no real "this order is now disputed" state behind them — with an actual status transition (`OrderStatus.Disputed`) and a recorded `DisputeRaisedBy`/`DisputeReason`/`DisputeRaisedAt`.
- `PUT /api/orders/{id}/resolve-dispute` — **admin-only** (`[Authorize(Roles = "Admin")]`), deliberately not left to either party to self-resolve. Resolves to either `Fulfilled` (dispute didn't hold up) or `Refunded` (it did).
- Honest limitation, documented directly in the code: resolving a dispute as `Refunded` only changes the order's status — `IPaymentGateway` has no `RefundAsync` yet, because `MockPaymentGateway` never actually charged anything to refund. Before this goes live against a real gateway, an actual refund call needs to be wired into that endpoint, or "Refunded" orders would silently not be refunded — the same class of bug as the legacy payment stub, just relocated.

### Notifications
- `Notification` + `INotificationService`/`NotificationService`, injected into `BidsController` and `OrdersController` and `await`ed at every lifecycle event: new bid, counter-offer, accept, reject, cancel, order paid, order fulfilled, dispute raised, dispute resolved.
- This is a direct fix for a High-severity finding in the legacy audit: `CommonMethods.addPortalNotification`/`addUserNotification` were declared `async void` and called without `await` throughout `OrderService`/`BidApprovalService` — meaning an exception inside them became unobservable (able to crash the process depending on host) and a request could complete before the notification write finished, silently dropping notifications under load. `NotificationService`'s methods are real `async Task`, and every caller awaits them — a failure here propagates like any other exception instead of vanishing.
- `NotifyPharmacyAsync` fans a notification out to every user belonging to a pharmacy (mirroring the legacy behavior of notifying all of a pharmacy's portal users), by looking up `ApplicationUser`s via the shared `AppDbContext.Users` (inherited from `IdentityDbContext`) — no separate user-lookup table needed.
- `NotificationsController`: `GET /api/notifications/mine` (optionally `?unreadOnly=true`), `PUT /api/notifications/{id}/read`, `PUT /api/notifications/read-all` — all scoped to the calling user's own notifications only.

### PDF invoices
- `IInvoiceService`/`QuestPdfInvoiceService`, `GET /api/orders/{id}/invoice` — generates and streams back a one-page PDF invoice for a paid order (seller/buyer/product details, line item, platform fee, total, payment reference), ownership-checked the same way as the other order endpoints.
- The legacy app carried **five** PDF libraries (`iTextSharp`, `ABCpdf`, `Aspose.PDF`, `Ghostscript.NET`, `Rotativa`) — a repo-wide grep in the original audit found only `iTextSharp` was ever actually invoked; the other four were dead weight, two of them (`ABCpdf`, `Aspose.PDF`) carrying real commercial license fees for code that did nothing. QuestPDF is the only PDF dependency here.
- **Licensing caveat, called out explicitly rather than assumed away** (see the comment on `QuestPdfInvoiceService`): QuestPDF's free "Community" license only covers organizations under $1M USD annual gross revenue (or non-profit/open source). Confirm MedLoop's eligibility before this ships to production — this is the same class of licensing question the legacy audit flagged for `iTextSharp` (AGPL) and never resolved. Don't repeat that here.

## What's deliberately NOT here yet
Real-time push (these are polled/in-app notifications, not sockets/webhooks), email delivery of notifications, scheduled jobs, the POS module, and the long-tail collections (feedback, rewards, promo codes, appointments, disposer/technician/distributor workflows). Those get added incrementally per the phased migration plan. A **real** payment gateway integration (replacing `MockPaymentGateway`, and adding an actual refund path) is also still pending — see the gaps noted above before that swap happens.

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
