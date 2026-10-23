# API reference

The live, always-current contract is the OpenAPI document at `/openapi/v1.json`, browsable at `/swagger` when `Api:ExposeDocumentation` is on (it is in Development). This page is an overview.

**Conventions**

- JSON with camelCase properties; enums as strings; times as ISO 8601 with the SAST offset (`+02:00`); money in ZAR as numbers.
- Authenticate with `Authorization: Bearer <token>` from `/api/auth/login` or `/api/auth/register`.
- Errors are [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) problem details with `title`, `detail`, `status`, `traceId`, a stable `code` for domain errors and `errors` (field → messages) for validation failures.
- Status codes: `400` invalid input or business rule, `401` not signed in, `403` wrong role, `404` not found (including other customers' bookings), `409` conflict (overlap, concurrent change, illegal transition, duplicate review), `429` rate limited.
- Lists are paged: `page`, `pageSize` → `{ items, page, pageSize, totalCount, totalPages }`.

## Public

| Method | Path | Notes |
|---|---|---|
| GET | `/api/categories` | With vehicle counts |
| GET | `/api/locations` | Active branches |
| GET | `/api/vehicles` | Filters: `q, category, transmission, fuel, minSeats, minLuggage, maxDailyRate, pickupAt, returnAt, availableOnly, sort, page, pageSize` |
| GET | `/api/vehicles/featured` | Optional `pickupAt, returnAt` |
| GET | `/api/vehicles/{id}` | Specification, media, ratings |
| GET | `/api/vehicles/{id}/availability` | `pickupAt, returnAt` → available, reasons, next available |
| GET | `/api/vehicles/{id}/ratings` · `/reviews` | Reviews: `rating, sort, page` |
| GET | `/api/reviews` | Published reviews across vehicles |
| POST | `/api/bookings/quote` | Server-side price and availability for a window |
| POST | `/api/recommendations` | Ranked, explained recommendations (rate limited) |
| POST | `/api/recommendations/interpret` | Free text → structured preferences (rate limited) |
| POST | `/api/auth/register` · `/api/auth/login` | Return `{ token, expiresAt, user }` (rate limited) |
| POST | `/api/contact` | Contact form (rate limited) |
| GET | `/health` | Health check including the database |

## Signed-in customers

| Method | Path | Notes |
|---|---|---|
| GET | `/api/auth/me` | Current user and roles |
| POST | `/api/auth/logout` | |
| GET · PUT | `/api/account/profile` | |
| POST | `/api/account/change-password` | |
| GET | `/api/account/notifications` · POST `/read` | |
| POST | `/api/bookings` | Create (`201`); held for 30 minutes as `Pending` |
| GET | `/api/bookings/mine?scope=upcoming\|past\|cancelled\|all` | |
| GET | `/api/bookings/{reference}` | Own bookings only |
| POST | `/api/bookings/{reference}/pay` | Simulated payment. Requires `Idempotency-Key` header (8–100 chars). Body optional: `{ "simulateFailure": true }` to see the decline path |
| GET | `/api/bookings/{reference}/cancellation` | Fee and refund preview |
| POST | `/api/bookings/{reference}/cancel` | `{ reason }` |
| POST | `/api/bookings/{reference}/review` | Completed rentals only, once per booking |
| PUT · DELETE | `/api/reviews/{id}` | Own reviews, within the edit window |
| POST | `/api/reviews/{id}/report` | `{ reason }` |

## Administrators (`/api/admin`, role `Administrator`)

| Area | Endpoints |
|---|---|
| Vehicles | `GET/POST /vehicles`, `GET/PUT/DELETE /vehicles/{id}` (delete = archive), `PATCH /vehicles/{id}/status`, `GET /vehicles/{id}/history`, `POST /vehicles/{id}/media` (multipart: `file, kind, altText`), `PUT /vehicles/{id}/media/{mediaId}/primary`, `DELETE /vehicles/{id}/media/{mediaId}` |
| Maintenance | `GET/POST /maintenance`, `GET /maintenance/alerts`, `PUT /maintenance/{id}`, `POST /maintenance/{id}/start · /complete · /cancel` |
| Bookings | `GET /bookings`, `GET /bookings/export` (CSV), `GET /bookings/{reference}`, `POST /bookings/{reference}/status` `{ status, reason, odometerKm }` |
| Customers | `GET /customers`, `GET /customers/{id}`, `PATCH /customers/{id}/status` `{ isActive }`, `POST /customers/{id}/roles` `{ role, grant }`, `GET /audit-log` |
| Messages | `GET /messages?handled=`, `PATCH /messages/{id}` `{ isHandled }` |
| Reviews | `GET /reviews?status=reported\|published\|hidden\|removed\|all`, `POST /reviews/{id}/moderate` `{ action: publish\|hide, note }`, `POST /reviews/reanalyse` |
| Catalogue | `GET/POST /categories`, `PUT/DELETE /categories/{id}`, `GET/POST /locations`, `PUT /locations/{id}` |
| Analytics | `GET /analytics/dashboard`, `/demand`, `/utilisation`, `/revenue`, `/sentiment`, `/insights` (query: `from, to, category`), `/forecast?category=&horizonWeeks=`, `/export/{utilisation\|revenue\|demand}` (CSV) |

## Example

```http
POST /api/bookings/quote
Content-Type: application/json

{
  "vehicleId": "…",
  "pickupAt": "2026-12-18T09:00:00+02:00",
  "returnAt": "2026-12-27T09:00:00+02:00",
  "pickupLocationId": 1,
  "returnLocationId": 3
}
```

The response itemises the rental, the 10% long-rental discount, the one-way fee and the VAT included, and reports whether the car is available (with reasons if not).
