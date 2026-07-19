# MedLoop NextGen

A from-scratch parallel implementation proving out the "next version" stack proposed for `PharmacyWebapp`: ASP.NET Core 8, PostgreSQL, and real authentication — instead of Firestore and the legacy app's ad hoc session-based auth.

This is **not** a fork of `PharmacyWebapp` and contains none of its code. It's a clean skeleton meant to be extended module by module, following the incremental migration plan written up for the legacy app.

## What's in this skeleton
- ASP.NET Core 8 Web API project.
- PostgreSQL via EF Core + Npgsql, with migrations applied automatically on startup in Development.
- Real authentication via ASP.NET Core Identity (`AddIdentityApiEndpoints` — built-in `/register`, `/login`, `/refresh` endpoints), with role support wired up (`AddRoles<IdentityRole>()`).
- Two entities as a proof of concept: `ApplicationUser` (extends `IdentityUser`) and `Pharmacy`, with a real foreign-key relationship and EF Core migrations — not a document store with string IDs standing in for relations.
- `PharmaciesController` — a minimal CRUD example that's actually `[Authorize]`-protected and validates its input.
- `Dockerfile` that runs as a non-root user out of the box.
- `docker-compose.yml` for local development against a real Postgres instance — no cloud account needed to run this locally.

## What's deliberately NOT here yet
Everything else — the ~45 other data collections/entities from the legacy app, the MedLoop Connect bidding flow, payments, PDF generation, email, scheduled jobs, the POS module, and so on. Per the phased migration plan, those get added incrementally, one module at a time, only once this foundation is confirmed solid.

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
