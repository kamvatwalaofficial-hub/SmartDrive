using System.Text.RegularExpressions;

namespace SmartDrive.Domain.Analytics;

public sealed record SentimentResult(
    string Label,
    decimal Score,
    IReadOnlyList<string> PositiveThemes,
    IReadOnlyList<string> NegativeThemes,
    string Method);

/// <summary>
/// A transparent lexicon-based sentiment and theme classifier for review text.
///
/// Each sentence is scored by counting positive and negative words (a negation such as "not" or
/// "wasn't" within three words flips the next sentiment word). Sentences are tagged with themes by
/// keyword, and each theme inherits the sentence's polarity. This is deliberately simple and will
/// sometimes be wrong (sarcasm, mixed sentences); admins always see the original review.
/// </summary>
public static partial class SentimentAnalyzer
{
    public const string Method = "lexicon-v1";

    public static readonly IReadOnlyDictionary<string, string[]> Themes = new Dictionary<string, string[]>
    {
        ["Cleanliness"] = ["clean", "dirty", "spotless", "smell", "smelly", "smelled", "stain", "stained", "dust", "dusty", "fresh", "tidy", "sand", "crumbs", "filthy", "valeted"],
        ["Vehicle comfort"] = ["comfortable", "uncomfortable", "comfort", "seats", "seat", "smooth", "quiet", "noisy", "roomy", "spacious", "legroom", "aircon", "air conditioning", "ride", "cramped"],
        ["Customer service"] = ["staff", "service", "friendly", "rude", "helpful", "unhelpful", "agent", "team", "counter", "desk", "communication", "support", "courteous", "attitude"],
        ["Booking experience"] = ["booking", "booked", "website", "app", "online", "checkout", "pickup", "pick-up", "collection", "collected", "return", "returned", "process", "queue", "wait", "waited", "waiting", "paperwork", "deposit", "handover"],
        ["Vehicle condition"] = ["condition", "scratch", "scratches", "scratched", "dent", "dents", "damaged", "damage", "broken", "faulty", "tyre", "tyres", "engine", "brakes", "warning light", "maintained", "rattle", "rattling", "worn", "new", "mileage"],
        ["Value for money"] = ["price", "value", "expensive", "cheap", "affordable", "overpriced", "worth", "cost", "fees", "rate", "money", "pricey", "bargain"],
    };

    private static readonly HashSet<string> Positive =
    [
        "clean", "spotless", "comfortable", "smooth", "great", "excellent", "friendly", "helpful", "easy", "quick",
        "fast", "efficient", "reliable", "good", "perfect", "amazing", "recommend", "recommended", "professional",
        "affordable", "fresh", "roomy", "spacious", "quiet", "love", "loved", "pleasant", "seamless", "painless",
        "maintained", "fantastic", "wonderful", "superb", "courteous", "bargain", "worth", "new", "tidy", "valeted",
        "happy", "impressed", "brilliant", "outstanding", "convenient", "nice", "best",
    ];

    private static readonly HashSet<string> Negative =
    [
        "dirty", "smelly", "stain", "stained", "uncomfortable", "noisy", "rude", "slow", "late", "delay", "delayed",
        "broken", "damaged", "scratch", "scratched", "scratches", "dent", "dents", "expensive", "overpriced", "poor",
        "bad", "terrible", "worst", "problem", "problems", "issue", "issues", "faulty", "leak", "unhelpful",
        "confusing", "difficult", "rattle", "rattling", "worn", "filthy", "cramped", "disappointed", "disappointing",
        "awful", "horrible", "waited", "queue", "pricey", "dusty", "unreliable", "frustrating", "annoying",
    ];

    private static readonly string[] NegativePhrases = ["warning light", "hidden fees", "hidden fee", "took ages", "long wait"];

    private static readonly HashSet<string> Negations = ["not", "no", "never", "wasn't", "isn't", "didn't", "doesn't", "weren't", "hardly", "barely", "nothing", "without"];

    public static SentimentResult Analyse(string? title, string? comment)
    {
        var text = $"{title}. {comment}".ToLowerInvariant();
        var positiveThemes = new HashSet<string>();
        var negativeThemes = new HashSet<string>();
        double positive = 0, negative = 0;

        foreach (var sentence in SentenceSplit().Split(text).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var words = WordRegex().Matches(sentence).Select(m => m.Value).ToArray();
            double sentencePolarity = 0;
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                var value = Positive.Contains(w) ? 1 : Negative.Contains(w) ? -1 : 0;
                if (value == 0)
                {
                    continue;
                }

                var negated = false;
                for (var j = Math.Max(0, i - 3); j < i; j++)
                {
                    if (Negations.Contains(words[j]) || words[j].EndsWith("n't", StringComparison.Ordinal))
                    {
                        negated = true;
                    }
                }

                sentencePolarity += negated ? -value : value;
            }

            foreach (var phrase in NegativePhrases)
            {
                if (sentence.Contains(phrase, StringComparison.Ordinal))
                {
                    sentencePolarity -= 1;
                }
            }

            if (sentencePolarity > 0) positive += sentencePolarity;
            if (sentencePolarity < 0) negative += -sentencePolarity;

            if (sentencePolarity == 0)
            {
                continue;
            }

            foreach (var (theme, keywords) in Themes)
            {
                var mentioned = keywords.Any(k => k.Contains(' ', StringComparison.Ordinal)
                    ? sentence.Contains(k, StringComparison.Ordinal)
                    : words.Contains(k));
                if (mentioned)
                {
                    (sentencePolarity > 0 ? positiveThemes : negativeThemes).Add(theme);
                }
            }
        }

        var total = positive + negative;
        var score = total == 0 ? 0 : (positive - negative) / total;
        var label = score >= 0.25 ? "Positive" : score <= -0.25 ? "Negative" : total == 0 ? "Neutral" : "Mixed";

        return new SentimentResult(
            label,
            Math.Round((decimal)score, 2),
            positiveThemes.OrderBy(t => t).ToList(),
            negativeThemes.OrderBy(t => t).ToList(),
            Method);
    }

    [GeneratedRegex(@"[.!?;\n]+|\bbut\b")] private static partial Regex SentenceSplit();
    [GeneratedRegex(@"[a-z]+(?:[-'][a-z]+)*")] private static partial Regex WordRegex();
}
