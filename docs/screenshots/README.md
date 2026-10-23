# Screenshots

Screenshots are produced by the end-to-end job in CI (`.github/workflows/ci.yml` → *End-to-end smoke test and screenshots*) from a fresh, seeded SQLite instance, so they always show the current build and only sample data.

To get them: open the latest successful CI run → **Artifacts** → `screenshots`. To regenerate locally, see [testing](../testing.md#running-the-end-to-end-smoke-test-locally); files are written to this folder.

| File | Shows |
|---|---|
| `01-home.png` | Home: hero, quick search, featured vehicles |
| `02-browse.png` | Fleet browsing with availability for chosen dates |
| `03-vehicle.png` · `04-vehicle-3d.png` | Vehicle page with server-priced quote; labelled demo 3D view |
| `05-wizard-interpret.png` · `06-wizard-results.png` | Recommendation wizard: interpreted free text; ranked results with score breakdown |
| `07-checkout.png` · `08-payment.png` · `09-my-bookings.png` | Booking flow with simulated payment |
| `10-admin-dashboard.png` | Operations dashboard |
| `11-insights.png` – `15-forecast.png` | Smart Insights: findings, utilisation, revenue, sentiment, forecast vs baseline |
| `16-fleet.png` · `17-maintenance.png` · `18-reviews.png` | Fleet, maintenance alerts, review moderation |
| `19-mobile-home.png` | Mobile layout |
