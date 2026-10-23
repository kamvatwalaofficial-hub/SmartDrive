namespace SmartDrive.Domain.Analytics;

public sealed record ForecastEvaluation(string Method, double MeanAbsoluteError);

public sealed record DemandForecast(
    bool HasEnoughData,
    int HistoryWeeks,
    IReadOnlyList<double> Forecast,
    string ChosenMethod,
    IReadOnlyList<ForecastEvaluation> Evaluations,
    double? Alpha,
    double? Beta,
    string Note);

/// <summary>
/// Weekly booking-demand forecasting with an honest comparison against a baseline.
///
/// <list type="number">
/// <item>Needs at least <see cref="MinimumWeeks"/> weeks of history; otherwise no forecast is made.</item>
/// <item>The last <see cref="HoldoutWeeks"/> weeks are held out. Two models are fitted on the rest:
///   a <b>baseline</b> (the average of the last four weeks, carried forward) and <b>Holt's linear
///   exponential smoothing</b> (level + trend, with α and β chosen by grid search to minimise one-step
///   squared error).</item>
/// <item>Both forecast the held-out weeks; the model with the lower mean absolute error (MAE) is
///   chosen (ties go to the simpler baseline) and refitted on the full history.</item>
/// </list>
/// Forecasts are estimates, not guaranteed bookings.
/// </summary>
public static class DemandForecaster
{
    public const int MinimumWeeks = 16;
    public const int HoldoutWeeks = 4;
    public const string Baseline = "4-week moving average (baseline)";
    public const string Holt = "Holt's linear exponential smoothing";

    public static DemandForecast Forecast(IReadOnlyList<double> weekly, int horizon = 4)
    {
        if (weekly.Count < MinimumWeeks)
        {
            return new DemandForecast(false, weekly.Count, [], "None", [], null, null,
                $"Forecasting needs at least {MinimumWeeks} weeks of booking history; {weekly.Count} available.");
        }

        var train = weekly.Take(weekly.Count - HoldoutWeeks).ToList();
        var test = weekly.Skip(weekly.Count - HoldoutWeeks).ToList();

        var baselineMae = Mae(MovingAverage(train, HoldoutWeeks), test);
        var (alpha, beta) = FitHolt(train);
        var holtMae = Mae(HoltForecast(train, alpha, beta, HoldoutWeeks), test);

        var useHolt = holtMae < baselineMae;
        var (fullAlpha, fullBeta) = FitHolt(weekly);
        var forecast = useHolt
            ? HoltForecast(weekly, fullAlpha, fullBeta, horizon)
            : MovingAverage(weekly, horizon);
        (alpha, beta) = (fullAlpha, fullBeta);

        var evaluations = new List<ForecastEvaluation>
        {
            new(Baseline, Math.Round(baselineMae, 2)),
            new(Holt, Math.Round(holtMae, 2)),
        };

        var note = useHolt
            ? $"Holt's method beat the baseline on the last {HoldoutWeeks} weeks (MAE {holtMae.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} vs {baselineMae.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} bookings per week)."
            : $"The simple baseline was at least as accurate as Holt's method on the last {HoldoutWeeks} weeks (MAE {baselineMae.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} vs {holtMae.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}), so the baseline is used.";

        return new DemandForecast(
            true,
            weekly.Count,
            forecast.Select(v => Math.Round(Math.Max(0, v), 1)).ToList(),
            useHolt ? Holt : Baseline,
            evaluations,
            useHolt ? alpha : null,
            useHolt ? beta : null,
            note + " Forecasts are estimates, not guaranteed bookings.");
    }

    public static List<double> MovingAverage(IReadOnlyList<double> series, int horizon)
    {
        var window = series.Skip(Math.Max(0, series.Count - 4)).ToList();
        var avg = window.Count == 0 ? 0 : window.Average();
        return Enumerable.Repeat(avg, horizon).ToList();
    }

    public static List<double> HoltForecast(IReadOnlyList<double> series, double alpha, double beta, int horizon)
    {
        var (level, trend) = HoltState(series, alpha, beta, out _);
        return Enumerable.Range(1, horizon).Select(h => level + (h * trend)).ToList();
    }

    public static (double Alpha, double Beta) FitHolt(IReadOnlyList<double> series)
    {
        var best = (Alpha: 0.5, Beta: 0.1);
        var bestError = double.MaxValue;
        for (var a = 0.1; a <= 0.91; a += 0.1)
        {
            for (var b = 0.05; b <= 0.51; b += 0.05)
            {
                HoltState(series, a, b, out var sse);
                if (sse < bestError)
                {
                    bestError = sse;
                    best = (Math.Round(a, 2), Math.Round(b, 2));
                }
            }
        }

        return best;
    }

    private static (double Level, double Trend) HoltState(IReadOnlyList<double> y, double alpha, double beta, out double sse)
    {
        sse = 0;
        if (y.Count == 0)
        {
            return (0, 0);
        }

        if (y.Count == 1)
        {
            return (y[0], 0);
        }

        var level = y[0];
        var trend = y[1] - y[0];
        for (var t = 1; t < y.Count; t++)
        {
            var predicted = level + trend;
            var error = y[t] - predicted;
            sse += error * error;
            var previousLevel = level;
            level = (alpha * y[t]) + ((1 - alpha) * (level + trend));
            trend = (beta * (level - previousLevel)) + ((1 - beta) * trend);
        }

        return (level, trend);
    }

    public static double Mae(IReadOnlyList<double> predicted, IReadOnlyList<double> actual) =>
        actual.Count == 0 ? 0 : actual.Select((a, i) => Math.Abs(a - predicted[i])).Average();
}
