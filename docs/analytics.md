# Analytics and Smart Insights

Source: `backend/src/SmartDrive.Domain/Analytics/*` (pure, unit-tested) and `backend/src/SmartDrive.Application/Services/AnalyticsService.cs`. All figures are computed on request from the database. Periods use South African dates; the default is the last 180 days.

> In the demo every figure is computed from generated sample data, which has seasonal patterns built in. The numbers illustrate the method; they are not results from a real business.

## Definitions

| Metric | Definition |
|---|---|
| **Booked days** | Days covered by confirmed, active or completed bookings inside the period. Overlapping intervals are merged so a day is never counted twice. |
| **Available days** | Days the vehicle was in the fleet during the period, minus days it was out of service for maintenance. |
| **Utilisation** | Booked days ÷ available days, per vehicle, per category and fleet-wide (totals, not an average of ratios). |
| **Realised revenue** | Totals of completed rentals plus retained cancellation fees, by pickup date. VAT-inclusive. |
| **Booked revenue** | Totals of confirmed or active rentals that are not yet completed. |
| **Cancellation rate** | Cancelled bookings ÷ bookings counted for the period. |
| **Demand** | Bookings by pickup date (SAST), excluding rejected bookings and expired unpaid holds: per month, per category, per weekday; weekday vs weekend split. |

The exact wording of each definition is returned by the API with the data and shown under each chart.

## Sentiment

`SentimentAnalyzer` is a transparent lexicon classifier:

1. Text is split into sentences and words.
2. Positive and negative words are counted; a negation (“not”, “wasn't”, “never”, …) within three words flips the next sentiment word.
3. A review gets a score; it is labelled **Positive** at ≥ 0.25, **Negative** at ≤ −0.25, **Neutral** when no sentiment words were found, and **Mixed** otherwise.
4. Sentences are tagged with **themes** by keyword — cleanliness, vehicle comfort, customer service, booking experience, vehicle condition, value for money — and each theme inherits the sentence's polarity.

It will sometimes be wrong (sarcasm, unusual phrasing). It is used for triage and aggregate trends only: administrators always see the original text, and nothing is hidden or published automatically because of a label. Reviews can be re-analysed after the lexicon changes.

## Forecast

`DemandForecaster` forecasts weekly bookings, honestly compared against a baseline:

1. It needs at least **16 weeks** of history; with less, no forecast is shown.
2. The last **4 weeks are held out**. Two methods are fitted on the earlier weeks:
   - **Baseline:** the mean of the last 4 weeks, carried forward.
   - **Holt's linear exponential smoothing** (level + trend), with α and β chosen by grid search to minimise one-step-ahead squared error.
3. Both forecast the held-out weeks; the method with the lower **mean absolute error** is chosen (ties go to the simpler baseline) and refitted on the full history.
4. The UI shows both errors, the chosen method and its parameters. If the baseline wins, the baseline is what is shown — the fancier model is not used just because it is fancier.

Forecasts are estimates; they do not account for holidays, events, price changes or turned-away demand.

## Insights

`InsightGenerator` turns metrics into findings only when the data crosses a threshold; otherwise it says nothing. Each insight carries an **area, severity, finding, period, evidence, suggested action, confidence and limitations**. Confidence is based on sample size: fewer than 20 observations = Low, fewer than 100 = Medium, otherwise High.

| Insight | Fires when |
|---|---|
| Category utilisation gap | Two categories with ≥ 5 bookings differ in utilisation by ≥ 15 percentage points |
| Rarely booked vehicles | Utilisation < 25% |
| Nearly fully booked vehicles | Utilisation ≥ 85% |
| High cancellation rate | ≥ 15% of bookings, with ≥ 20 bookings in the period |
| Weekend or weekday peak | Pickups per day in one part of the week are ≥ 1.5× the other, with ≥ 20 pickups |
| Recurring review complaint | A theme is mentioned negatively in ≥ 3 reviews and more often negatively than positively |
| Overdue / upcoming services | Past the due date or mileage / due within 14 days or 1,000 km |
| Repeated issues | ≥ 2 reported faults or repairs on one vehicle within 90 days |

Insights describe patterns, not causes, and say so.

## Exports

CSV exports are available for bookings (with the current filters), utilisation, revenue and demand. Cells that a spreadsheet could interpret as formulas are escaped.
