using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDrive.Api.Infrastructure;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Services;

namespace SmartDrive.Api.Controllers;

/// <summary>
/// The explainable recommendation engine. Availability, prices and specifications always come from the
/// database and the booking rules; free text is only ever turned into preferences.
/// </summary>
[Route("api/recommendations")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Recommendations)]
public sealed class RecommendationsController(RecommendationService recommendations) : ApiControllerBase
{
    /// <summary>Ranks genuinely available vehicles against the customer's preferences, with reasons and trade-offs.</summary>
    [HttpPost]
    public Task<RecommendationResponseDto> Recommend(RecommendationRequest request, CancellationToken ct) =>
        recommendations.RecommendAsync(request, CurrentUser.UserId, ct);

    /// <summary>
    /// Turns a sentence such as "a comfortable car for four adults for five days, under R600 a day" into
    /// structured preferences for the customer to confirm. Uses rules, or a language model when configured.
    /// </summary>
    [HttpPost("interpret")]
    public Task<InterpretedPreferencesDto> Interpret(InterpretRequest request, CancellationToken ct) =>
        recommendations.InterpretAsync(request.Text, ct);
}
