# Limitations

SmartDrive is a portfolio project. It is built to production standards where that matters for correctness and security, but it is not running a real business. Known limitations:

**Business**
- **Payments are simulated.** There is no payment provider, no card capture and no real refunds; the deposit is shown but never held. Integrating a provider (e.g. via hosted checkout and webhooks) would replace `PaySimulatedAsync`.
- **No email or SMS.** Notifications are stored in the app only; the contact form is an admin inbox.
- **No driver verification.** Licence details are self-declared; there is no identity or licence check, insurance product, damage inspection or fuel policy.
- **Pricing is simple:** one daily rate per vehicle, fixed discounts and a flat one-way fee. No seasonal or dynamic pricing, extras (child seats, additional drivers), promo codes or fleet-wide availability by branch — a car is not tied to a branch between rentals.

**Data and "AI"**
- **All data is generated sample data**, with seasonality and issues built in on purpose. Analytics demonstrate the methods; they are not findings about any real company.
- **Recommendation weights are hand-set**, not learned from outcomes.
- **Sentiment is lexicon-based** and English-only; it misreads sarcasm and unusual phrasing.
- **The forecast is univariate** and ignores public holidays, school terms, events and turned-away demand. It is only shown with at least 16 weeks of history.
- **The optional language-model parser** depends on an external service when configured; it only extracts preferences and falls back to rules on failure.
- **3D:** no licensed 3D vehicle models are included. The 3D view shows a generic demo shape, labelled as such, unless an administrator attaches a model.
- **Vehicle images are generated illustrations**, not photographs.

**Technical**
- **Session tokens live in `localStorage`** and expire after two hours with no refresh token. An HttpOnly-cookie session or a refresh-token flow would reduce exposure to XSS.
- **Single-instance assumptions:** rate limits and the in-process media store are per instance. Multiple instances would need a distributed rate limiter and blob storage.
- **Times are SAST only.** Branches outside South Africa would need per-branch time zones.
- **SQLite mode** (quick demo and tests) stores money as floating point and uses `EnsureCreated` rather than migrations; SQL Server is the supported database.
- **Accessibility** has been considered (labels, focus states, keyboard navigation, reduced motion, data tables behind every chart) but not audited with assistive-technology users.
- **Not load-tested.** No performance figures are claimed.
