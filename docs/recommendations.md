# Recommendation engine

Source: `backend/src/SmartDrive.Domain/Recommendations/RecommendationEngine.cs` (pure, unit-tested) and `RecommendationService.cs` (loads the fleet, prices each vehicle and checks availability for the requested window).

The engine is deliberately rule-based so that every result can be explained and checked. It has four stages.

## 1. Candidates

For the requested pickup and return time (and branches), the service builds a candidate for every active vehicle with:

- a **server-side quote** from the same `PricingCalculator` used for real bookings (rental days, discounts, one-way fee, total, effective daily rate);
- **availability** from the same `AvailabilityRules` used for real bookings, plus the next available pickup time if it is not free;
- its specification, comfort rating, fuel economy and review summary.

## 2. Eligibility — hard constraints

A vehicle is a **match** only if all of these hold. A high score can never override them.

| Constraint | Rule |
|---|---|
| Availability | Free for the whole window, including turnaround, holds and maintenance |
| Passengers | `seats ≥ passengers` |
| Luggage | `luggage capacity ≥ bags` |
| Transmission | Must match, **only if** the customer said they can only drive that type |
| Budget | `max(effective daily rate ÷ daily budget, total ÷ total budget) ≤ 1` |

## 3. Scoring — soft preferences

Each match gets a score from 0 to 100: a **weighted average of only the factors the customer actually expressed**, plus value for money (always) and customer rating (only when the vehicle has at least three reviews). Each factor scores between 0 and 1.

| Factor | Weight | Factor score |
|---|---:|---|
| Budget fit | 25 | `1 − 0.5 × r` when within budget (r = budget ratio above), so cheaper relative to budget scores higher |
| Category | 20 | 1 for the preferred category; partial credit for related ones (e.g. SUV ↔ minivan); otherwise 0 |
| Passenger fit | 15 | `0.6 + 0.4 × passengers ÷ seats` — enough seats without needless size |
| Luggage fit | 10 | `0.6 + 0.4 × bags ÷ capacity` |
| Transmission | 10 | 1 if it matches a stated preference, else 0 |
| Fuel type | 5 | 1 if it matches, else 0 |
| Journey suitability | 10 | Category suitability for city, business, family or road trip, blended with comfort, luggage, seats or efficiency |
| Comfort | 10 | `comfort rating ÷ 5` (only when prioritised) |
| Fuel efficiency | 10 | Electric = 1; otherwise scaled between the most and least economical candidate (only when prioritised) |
| Space | 10 | `0.6 × luggage ÷ max luggage + 0.4 × seats ÷ max seats` (only when prioritised) |
| Value for money | 10 | Cheapest suitable total = 1, most expensive = 0 |
| Customer rating | 5 | `average ÷ 5`, only with ≥ 3 reviews |

`score = round(100 × Σ(weight × factor) ÷ Σ(weight))`, over the factors present. Ties are broken by the lower total price, and up to six matches are returned. The UI shows each factor, its share of the weighting and its score, so a customer can see exactly why one car ranks above another.

## 4. Explanations and alternatives

- **Reasons and trade-offs** are generated from the vehicle's own data and the verified quote, e.g. “Seats 5 — room for your 4 passengers”, “R640.00 per day — within your R700.00 daily budget”, “Manual transmission (you preferred automatic)”. Nothing is claimed that the data does not support.
- When **nothing** passes every hard constraint, the engine says which requirement could not be met and suggests what to relax, then offers up to three **alternatives**, in this order: available and big enough but over budget (least over first); available but with the other transmission (when it was required); big enough but booked, shown **only with its next available date** — never presented as available.

## Natural-language input

The wizard's optional first step turns a sentence such as *“A comfortable car for four adults for a five-day trip, no more than R600 per day”* into structured preferences, which the customer then reviews and can edit before anything is scored.

- **Default — rule-based parser** (`PreferenceParser`): regular expressions and keyword lists for passengers (digits and number words, “family of five”, “couple”), bags, duration, daily or total budget in Rand, category, transmission (and whether it is required), fuel, journey type and priorities. It returns an “understood” list and notes about anything ambiguous.
- **Optional — language model** (`LanguageModelPreferenceInterpreter`): if `Ai:LanguageModel` is configured with any OpenAI-compatible endpoint, the text is sent with a strict instruction to return JSON preferences only. The response is parsed, every field is range-checked and mapped to allowed values (`ParsedPreferences.Sanitised`), and anything rejected is reported. On timeout, error or invalid output it falls back to the rule-based parser. **The model never sees the fleet, prices or availability, and never chooses vehicles.** The UI shows which interpreter was used.

## What it does not do

It does not learn from past bookings or personalise by customer history, and its weights are hand-set rather than fitted. Request preferences are logged without personal data (`RecommendationLogs`) so the weights could be evaluated later.
