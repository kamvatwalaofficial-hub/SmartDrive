using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Recommendations;

namespace SmartDrive.Application.Ai;

public sealed record Interpretation(ParsedPreferences Preferences, string Source);

/// <summary>
/// Turns a free-text request into structured preferences. Implementations must only extract
/// preferences: vehicle facts (availability, prices, specifications) always come from the database
/// through the normal recommendation engine.
/// </summary>
public interface IPreferenceInterpreter
{
    Task<Interpretation> InterpretAsync(string text, CancellationToken ct);
}

/// <summary>The default: transparent rules, no external service, always available.</summary>
public sealed class RuleBasedPreferenceInterpreter : IPreferenceInterpreter
{
    public Task<Interpretation> InterpretAsync(string text, CancellationToken ct) =>
        Task.FromResult(new Interpretation(PreferenceParser.Parse(text), "rules"));
}

public sealed class LanguageModelOptions
{
    /// <summary>OpenAI-compatible chat completions endpoint, e.g. https://api.openai.com/v1.</summary>
    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Optional language-model interpreter for any OpenAI-compatible chat-completions API. It is only
/// used when <see cref="LanguageModelOptions"/> are configured. The model's JSON is parsed strictly,
/// every value is range-checked, and on any failure the rule-based interpreter is used instead.
/// </summary>
public sealed class LanguageModelPreferenceInterpreter(
    HttpClient http,
    LanguageModelOptions options,
    RuleBasedPreferenceInterpreter fallback,
    ILogger<LanguageModelPreferenceInterpreter> logger) : IPreferenceInterpreter
{
    private const string SystemPrompt =
        """
        You extract car-rental preferences from a customer's message for a South African rental company.
        Reply with ONE JSON object and nothing else, using only these keys (omit unknown ones):
        passengers (int), luggageBags (int), durationDays (int), maxDailyBudget (number, ZAR),
        maxTotalBudget (number, ZAR), category ("economy"|"hatchback"|"sedan"|"suv"|"minivan"|"bakkie"|"luxury"),
        transmission ("Automatic"|"Manual"), transmissionRequired (bool), fuel ("Petrol"|"Diesel"|"Hybrid"|"Electric"),
        journey ("City"|"Business"|"Family"|"RoadTrip"), prioritiseComfort (bool), prioritiseFuelEfficiency (bool),
        prioritiseSpace (bool).
        Never invent vehicles, prices or availability. If the message is not about renting a car, reply {}.
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    private static readonly string[] Categories = ["economy", "hatchback", "sedan", "suv", "minivan", "bakkie", "luxury"];

    public async Task<Interpretation> InterpretAsync(string text, CancellationToken ct)
    {
        var rules = await fallback.InterpretAsync(text, ct);
        if (!options.IsConfigured)
        {
            return rules;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl!.TrimEnd('/')}/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = options.Model,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user", content = text.Length > 1000 ? text[..1000] : text },
                    },
                }),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);

            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var completion = await response.Content.ReadFromJsonAsync<ChatCompletion>(Json, timeout.Token);
            var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return rules;
            }

            var extracted = JsonSerializer.Deserialize<ModelPreferences>(content, Json) ?? new ModelPreferences();
            var parsed = new ParsedPreferences
            {
                Passengers = extracted.Passengers,
                LuggageBags = extracted.LuggageBags,
                DurationDays = extracted.DurationDays,
                MaxDailyBudget = extracted.MaxDailyBudget,
                MaxTotalBudget = extracted.MaxTotalBudget,
                CategorySlug = extracted.Category is { } c && Categories.Contains(c.ToLowerInvariant()) ? c.ToLowerInvariant() : null,
                Transmission = extracted.Transmission,
                TransmissionRequired = extracted.TransmissionRequired ?? false,
                Fuel = extracted.Fuel,
                Journey = extracted.Journey,
                PrioritiseComfort = extracted.PrioritiseComfort ?? false,
                PrioritiseFuelEfficiency = extracted.PrioritiseFuelEfficiency ?? false,
                PrioritiseSpace = extracted.PrioritiseSpace ?? false,
            };

            var sanitised = parsed.Sanitised(out var rejected);
            return new Interpretation(
                sanitised with
                {
                    Understood = rules.Preferences.Understood,
                    Notes = [.. rejected, "Interpreted with a language model; please check the values below."],
                },
                "language-model");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Language-model interpretation failed; using rule-based interpretation");
            return rules;
        }
    }

    private sealed record ChatCompletion(List<Choice>? Choices);

    private sealed record Choice(ChatMessage? Message);

    private sealed record ChatMessage(string? Content);

    private sealed record ModelPreferences
    {
        public int? Passengers { get; init; }
        public int? LuggageBags { get; init; }
        public int? DurationDays { get; init; }
        public decimal? MaxDailyBudget { get; init; }
        public decimal? MaxTotalBudget { get; init; }
        public string? Category { get; init; }
        public TransmissionType? Transmission { get; init; }
        public bool? TransmissionRequired { get; init; }
        public FuelType? Fuel { get; init; }
        public JourneyType? Journey { get; init; }
        public bool? PrioritiseComfort { get; init; }
        public bool? PrioritiseFuelEfficiency { get; init; }
        public bool? PrioritiseSpace { get; init; }
    }
}
