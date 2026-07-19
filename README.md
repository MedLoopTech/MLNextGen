# MedLoop NextGen

A from-scratch parallel implementation proving out the "next version" stack proposed for `PharmacyWebapp`: ASP.NET Core 8, PostgreSQL, and real authentication — instead of Firestore and the legacy app's ad hoc session-based auth.

This is **not** a fork of `PharmacyWebapp` and contains none of its code. It's a clean skeleton meant to be extended module by module, following the incremental migration plan written up for the legacy app.

## What's in this skeleton
- ASP.NET Core 8 Web API project, **plus a Blazor Server portal** (see below) in the same project.
- PostgreSQL via EF Core + Npgsql, with migrations applied automatically on startup in Development.
- Real authentication via ASP.NET Core Identity (`AddIdentityApiEndpoints` — built-in `/register`, `/login`, `/refresh` endpoints for API/SPA clients), with role support wired up (`AddRoles<IdentityRole>()`).
- `Dockerfile` that runs as a non-root user out of the box.
- `docker-compose.yml` for local development against a real Postgres instance — no cloud account needed to run this locally.

## ✅ Verified against a real Postgres instance, not just compiled
Earlier revisions of this README warned the portal had only been written, never run. That's no longer true: it's been built and driven end-to-end against a real local Postgres 16 database — register, browse the marketplace, place a bid, counter-offer both directions, accept, checkout, download the invoice PDF, list orders as both buyer and seller. That testing caught (and fixed) several real bugs that pure code review missed, including two startup-crashing DI/config issues, an enum-serialization bug, a JSON reference-cycle bug that hung the bid endpoints, and a real security bug where the other negotiating party's Identity password hash was leaking in plain JSON. See the git history on `Models/BidNegotiationRound.cs`, `Models/PosSaleItem.cs`, and `Program.cs`'s JSON options for the details.
That said, this is still a fast-moving skeleton, not a hardened production app — treat anything not explicitly called out as tested elsewhere in this README with appropriate caution.

## The B2B portal (Blazor Server)
Added in the same project as the API, not a separate frontend stack — same language, and it plugs directly into the ASP.NET Core Identity cookie auth already in place.

**Scoped to the B2B flow only, as requested** — it does not cover POS or the take-back/loyalty program yet.

- `/account/login`, `/account/register` — plain static HTML forms (not Blazor `EditForm`) posting to minimal-API endpoints in `Program.cs` that call `SignInManager` directly. Registering with a pharmacy name creates a new `Pharmacy` and joins it as staff; leaving it blank registers a plain consumer account.
- `/marketplace` — browse other pharmacies' `ForRedistribution` listings.
- `/marketplace/{id}` — listing detail + place a bid.
- `/bids/mine` — your bids as buyer: accept/reject/counter a seller's counter-offer, or cancel an open negotiation.
- `/bids/incoming` — bids on your own listings as seller: accept/reject/counter a buyer's offer.
- `/orders/mine` — approved bids ready to check out (with optional promo code), plus your purchase and sales history.
- `/orders/{id}` — order detail, invoice download (links directly to the existing `GET /api/orders/{id}/invoice` REST endpoint — the one place the portal reuses the API instead of duplicating logic, since the browser's own auth cookie covers that same-origin request), fulfill (seller), raise a dispute, leave feedback.

### Known architectural shortcut — the portal duplicates controller logic
To move fast, portal pages call `AppDbContext` and the domain services (`INotificationService`, `IPaymentGateway`, `ILoyaltyService`) **directly**, re-implementing the same validation/business rules that already exist in `BidsController`/`OrdersController` (ownership checks, stock/concurrency handling, server-side amount computation) rather than calling those controllers over HTTP. This was a deliberate tradeoff to avoid the complexity of forwarding the Identity auth cookie into internal API calls, but it means **the same business rule now has two places it could drift out of sync**. The right fix, once the portal is confirmed working: extract the logic currently living in `BidsController.Accept/Reject/Counter` and `OrdersController.Checkout` into shared service classes (e.g. `IBidWorkflowService`, extending the pattern already used by `ILoyaltyService`), and have both the API controllers and the portal call those services instead of duplicating the logic inline.

### Known gap — auth form CSRF protection
The plain login/register/logout forms use `.DisableAntiforgery()` in `Program.cs` because they don't currently include an antiforgery token. This should be fixed (via Blazor's `<AntiforgeryToken />` component plus server-side validation) before production — it's the same class of gap (missing CSRF protection) the legacy app's audit flagged as an actively exploitable pattern, just newly introduced here rather than carried over.

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
- Every notification also attempts to send an email via `IEmailSender` (see below) to the recipient's address. Email is treated as a best-effort secondary channel: a failed send is logged and swallowed rather than failing the notification call, since the in-app `Notification` row is always the source of truth. This is a narrow, deliberate exception to "don't swallow errors" — not the blanket catch-everything-silently pattern the legacy app's audit flagged throughout its controllers.

### Email
- `IEmailSender`/`GmailEmailSender` — sends mail via the Gmail API using a Google Workspace service account with domain-wide delegation, impersonating a real mailbox. No SMTP, no password, ever. This is the exact same fix already applied to the legacy `PharmacyWebapp`'s `EmailService` (which originally used raw SMTP with an App Password — the credential that leaked into that repo's git history).
- `IdentityEmailSenderAdapter` wires this into ASP.NET Core Identity's own `IEmailSender<TUser>` hook, which `AddIdentityApiEndpoints` uses for account-confirmation and password-reset emails. **Without this, those emails were silently no-ops** — Identity registers its own no-op sender internally via `TryAdd` if nothing else claims the slot, so this repo had that exact gap until this commit. Registered in `Program.cs` deliberately *after* `AddIdentityApiEndpoints(...)` so it wins.
- Setup required (same manual steps as `PharmacyWebapp`'s equivalent fix, not something this code can do on its own): enable the Gmail API on the GCP project holding your service account; authorize that service account's Client ID for domain-wide delegation with scope `https://www.googleapis.com/auth/gmail.send` in Workspace Admin Console; set `GOOGLE_APPLICATION_CREDENTIALS` to the service account key path and `Email:SenderAddress` (`appsettings.json`, currently an empty placeholder) to a real mailbox in that Workspace domain. Until configured, `GmailEmailSender` throws clearly on first use rather than failing silently or crashing the whole app at startup (it's a Scoped service, only constructed when something actually tries to send an email).

### PDF invoices
- `IInvoiceService`/`QuestPdfInvoiceService`, `GET /api/orders/{id}/invoice` — generates and streams back a one-page PDF invoice for a paid order (seller/buyer/product details, line item, platform fee, total, payment reference), ownership-checked the same way as the other order endpoints.
- The legacy app carried **five** PDF libraries (`iTextSharp`, `ABCpdf`, `Aspose.PDF`, `Ghostscript.NET`, `Rotativa`) — a repo-wide grep in the original audit found only `iTextSharp` was ever actually invoked; the other four were dead weight, two of them (`ABCpdf`, `Aspose.PDF`) carrying real commercial license fees for code that did nothing. QuestPDF is the only PDF dependency here.
- **Licensing caveat, called out explicitly rather than assumed away** (see the comment on `QuestPdfInvoiceService`): QuestPDF's free "Community" license only covers organizations under $1M USD annual gross revenue (or non-profit/open source). Confirm MedLoop's eligibility before this ships to production — this is the same class of licensing question the legacy audit flagged for `iTextSharp` (AGPL) and never resolved. Don't repeat that here.

### Scheduled jobs
- Quartz.NET (same library the legacy app used), wired in `Program.cs` with two daily jobs:
  - `NearExpiryNotificationJob` (08:00 UTC) — notifies a pharmacy when one of its own `ForRedistribution` listings is within 30 days of expiry.
  - `MarkExpiredListingsJob` (00:30 UTC) — marks listings past their expiry date as `Disposed` and notifies the owning pharmacy.
- Both jobs fix the exact scheduler bugs the legacy audit found in `EmaiService/SchedulerManager.cs`: every step is wrapped in try/catch and logged via `ILogger<T>` (not `Console.WriteLine`), and one bad row/notification failure logs and moves on instead of silently killing the entire run with nothing useful left in production logs to diagnose it.
- Job classes are registered `Scoped` in DI (`builder.Services.AddScoped<TJob>()`) alongside `q.AddJob<TJob>()` — required because `UseMicrosoftDependencyInjectionJobFactory()` resolves job instances directly from the container, giving each run a fresh `AppDbContext` rather than a long-lived one across runs.

### POS (in-person retail sales)
A separate subsystem from the B2B marketplace — the legacy app kept these in a distinct `Controllers/POS/` folder (`CustomerController`, `POSLoginController`, `ProductController`) for counter sales to walk-in customers, and this repo mirrors that separation under `Controllers/Pos/`.
- `Customer` — a pharmacy's own retail customer (walk-in/loyalty), distinct from `Pharmacy` (a marketplace participant) and `ApplicationUser` (someone who logs in). `CustomersController` (`api/pos/customers`) is pharmacy-scoped the same way `Branches`/`Warehouses` are.
- `PosSale`/`PosSaleItem` — a completed counter transaction and its line items. `SalesController.CreateSale` (`POST api/pos/sales`) takes only product IDs, quantities, an optional customer, and a payment method — **never a price or total**. Every line's price is read from `Product.Price` at the moment of sale and copied onto `PosSaleItem.UnitPriceAtSale` (so a later price change never retroactively changes a past receipt), and `PosSale.TotalAmount` is computed server-side from that. This is the same discipline as `OrdersController.Checkout`, applied here for the same reason: a client-suppliable total is the price-tampering bug in a different part of the app.
- POS sales decrement the same `Product.Quantity`/`AvailableQuantity` pool that B2B bids lock against, wrapped in a transaction with the same `xmin`-concurrency handling as checkout — selling a product at the counter and locking it for a B2B bid can't both succeed for stock that isn't actually there.

### B2C medicine take-back + loyalty points ("dawadaira")
The legacy project's GCP project IDs (`dawadaira-dev`/`dawadaira-live`) referred to this specifically: a **B2C** program, separate from both the B2B marketplace and POS above. A consumer — an `ApplicationUser` with no `PharmacyId`, not affiliated with any pharmacy — returns unused/excess medicine to a partner pharmacy; pharmacy staff verify it in person; a verified return earns the consumer loyalty points.
- `ApplicationUser.LoyaltyPoints` — a materialized balance. The real source of truth is `LoyaltyPointTransaction`, an append-only ledger (same audit-trail discipline as `BidNegotiationRound` for bids) — every earn and redemption is a row, not just a mutated number with no history.
- `TakeBackSubmission` + `TakeBackController`: `POST /api/takeback` (any logged-in consumer, no pharmacy affiliation required — this is the one controller in the app that deliberately does *not* require `PharmacyId`), `GET /api/takeback/mine` (consumer's own history), `GET /api/takeback/incoming` + `PUT /api/takeback/{id}/verify` / `/reject` (the partner pharmacy's queue, ownership-checked the same way bids are).
- Points awarded on verification are **always** `Quantity × Marketplace:LoyaltyPointsPerTakeBackUnit` (`appsettings.json`, default 10) — computed server-side, never entered manually by whoever is verifying, so the reward can't be arbitrarily inflated by a staff member (or a staff member colluding with the submitter).
- `ILoyaltyService`/`LoyaltyService` (`AwardPointsAsync`/`TryRedeemPointsAsync`) does the balance update and ledger insert together; `ApplicationUser` uses the same `xmin` optimistic concurrency token as `Product`, so two concurrent point changes on the same user can't produce a lost update.
- `LoyaltyController`: `GET /api/loyalty/balance` (current balance + recent ledger entries), `POST /api/loyalty/redeem` (deducts points, records the redemption).
- **Deliberate scope boundary**: `redeem` only deducts points and records the ledger entry — it does **not** hand back a discount code or credit usable anywhere, because this skeleton has no consumer-facing storefront yet for points to be spent in (checkout and POS are both pharmacy-side flows, not something a plain consumer buys through). Wiring redemption into an actual purchase happens once that storefront is designed — guessing at that mechanism now would just be inventing business logic nobody asked for.

### Promo codes
- `PromoCode` (admin-managed via `PromoCodesController`, `[Authorize(Roles = "Admin")]`): a percentage discount, optional expiry, optional max-redemption cap.
- Applied at `POST /api/orders/checkout` via an optional `promoCode` string field — the *only* thing the client sends about it. The discount percentage, validity, and redemption count are all read from the stored `PromoCode` row server-side; the discount is applied to the subtotal before the platform fee is calculated, and the resulting `DiscountAmount` is recorded on the `Order` (and shown on its invoice).
- Redemption counting has the same race-condition protection as stock locking: `PromoCode` also uses `UseXminAsConcurrencyToken()`, and the redemption count is re-checked and incremented inside the same DB transaction as the rest of checkout — two checkouts racing to use the last remaining redemption of a capped code can't both succeed.

### Order feedback
- `OrderFeedback` — one rating (1-5) + optional comment per `(Order, party)`, enforced both in application code and with a unique database index, so a duplicate or racing submission can't slip through either path.
- `POST /api/orders/{id}/feedback` / `GET /api/orders/{id}/feedback` — only valid once an order is `Fulfilled`; either party can leave feedback, and the other party gets notified when they do.
- Replaces the legacy app's implied-but-never-built feedback concept with something structured, rather than free-floating comment fields with no real state behind them (the same pattern already fixed for disputes).

## What's deliberately NOT here yet
Real-time push (these are polled/in-app notifications plus email, not sockets/webhooks), a consumer-facing storefront for spending redeemed loyalty points (see the scope boundary noted above), and the remaining long-tail collections (achievements/badges beyond points, appointments, disposer/technician/distributor workflows). Those get added incrementally per the phased migration plan. A **real** payment gateway integration (replacing `MockPaymentGateway`, and adding an actual refund path) is also still pending — see the gaps noted above before that swap happens.

## Running locally
```bash
docker compose up --build
```
The app comes up on `http://localhost:8080` (portal at `/`, Swagger UI at `/swagger` in Development). The `InitialCreate` migration is already committed under `Migrations/` and applies automatically against the `db` container on startup — no `dotnet ef migrations add` step needed before the first run.

### Demo accounts (seeded automatically in Development)
On first startup, `Data/DbSeeder.cs` creates three accounts and two marketplace listings so there's something to click on immediately, instead of a blank slate that needs manual registration first. Seeding is idempotent — it checks for each record before creating it, so restarting the app (or `docker compose up` again against the same volume) never duplicates anything.

| Email | Password | Role |
|---|---|---|
| `seller@medloop.test` | `MedLoopDemo123!` | Staff of "Demo Seller Pharmacy" — has two `ForRedistribution` listings (Paracetamol 500mg, Amoxicillin 250mg) ready to bid on. |
| `buyer@medloop.test` | `MedLoopDemo123!` | Staff of "Demo Buyer Pharmacy" — use this to place a bid on the seller's listings. |
| `admin@medloop.test` | `MedLoopDemo123!` | In the `Admin` role — can manage categories and resolve order disputes. |

These are dev-only convenience accounts with a shared, publicly-documented password — the seeder only runs when `ASPNETCORE_ENVIRONMENT=Development` (see `Program.cs`), and must never run against a production database.

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
