# Testing

| Suite | Location | Runs |
|---|---|---|
| Unit (domain + services) | `backend/tests/SmartDrive.UnitTests` | `dotnet test --project tests/SmartDrive.UnitTests` from `backend/` |
| Integration (whole API) | `backend/tests/SmartDrive.IntegrationTests` | `dotnet test --project tests/SmartDrive.IntegrationTests` |
| Frontend unit + component | `frontend/src/**/*.test.ts(x)` | `npm test` (watch) or `npm test -- --run` in `frontend/` |
| End-to-end smoke + screenshots | `frontend/e2e/smoke.mjs` | Against a running API and `vite preview`; see below |

All of them run in GitHub Actions on every push and pull request (`.github/workflows/ci.yml`), together with a build that treats warnings as errors, a vulnerable-package check, `has-pending-model-changes`, applying every migration to SQL Server 2022, ESLint, TypeScript and `npm audit`.

## What is covered

**Domain rules** — rental-day calculation with the grace period; discounts at 7 and 21 days; one-way fee; VAT extraction; every legal and illegal booking-state transition; cancellation fees either side of the 48-hour line; review eligibility; availability with turnaround, holds and maintenance; recommendation eligibility, scoring, explanations and alternatives; the free-text parser (including the example sentence from the brief); utilisation with overlaps and maintenance; service-due and repeated-issue rules; forecast minimum history and baseline comparison; sentiment and themes; insight thresholds.

**Services against a real (SQLite) database** — server-side pricing ignores anything from the client; overlapping bookings are refused; expired holds release the car; **eight concurrent bookings for one car produce exactly one booking**; the concurrency token rejects stale updates; customers cannot see or cancel each other's bookings; idempotent payments; late-cancellation fees; inactive accounts cannot book; reviews only after completion and only once; analytics counts.

**The HTTP API end to end** — `WebApplicationFactory` boots the real application with JWT auth, validation, authorisation and the full sample-data seed: health and OpenAPI; browsing and filtering; `401` without a token and `403` for customers on admin routes; validation errors; identical messages for wrong password and unknown email; immediate lock-out after deactivation; quote → book → pay → list; cross-customer `404`; double-booking `409`; recommendations only return available, in-budget cars with explanations; review once; analytics and CSV exports; and that the seeded history never violates the booking rules.

**Frontend** — SAST conversions regardless of the machine's time zone, Rand formatting, URL state for trips, the password policy mirror, server-error mapping, the vehicle card, sign-in validation and redirects, route guards, the recommendation wizard from free text to explained results, and that a payment retried after a network failure reuses the same idempotency key.

## Running the end-to-end smoke test locally

```bash
# terminal 1 — API in SQLite demo mode
cd backend
dotnet run --project src/SmartDrive.Api --launch-profile "SmartDrive.Api (SQLite quick demo)"

# terminal 2 — production build of the frontend
cd frontend
npm run build && npx vite preview --port 4173

# terminal 3
cd frontend
npx playwright install chromium
DEMO_ADMIN_PASSWORD=… DEMO_CUSTOMER_PASSWORD=… BASE_URL=http://localhost:4173 npm run e2e
```

The script walks the customer journey (search, vehicle, 3D view, wizard, checkout, simulated payment) and the admin area, saves screenshots to `docs/screenshots`, and fails on any console error, uncaught exception or `5xx` from the API.
