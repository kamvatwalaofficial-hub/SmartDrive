using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDrive.Application.Contracts;
using SmartDrive.Application.Data;
using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Rules;

namespace SmartDrive.IntegrationTests;

public sealed class ApiWorkflowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = ApiFactory.Json;

    private static DateTimeOffset LocalDay(int daysFromNow, int hour) =>
        new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(daysFromNow).AddHours(hour), DateTimeKind.Unspecified), TimeSpan.FromHours(2));

    // ------------------------------------------------------------------ public

    [Fact]
    public async Task Health_check_and_openapi_document_are_served()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
        var openApi = await client.GetStringAsync("/openapi/v1.json", Ct);
        Assert.Contains("/api/bookings", openApi);
        Assert.Contains("bearer", openApi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anyone_can_browse_the_fleet_and_filter_it()
    {
        var client = factory.CreateClient();

        var all = await (await client.GetAsync("/api/vehicles?pageSize=50", Ct)).ReadAsync<PagedResult<VehicleSummaryDto>>();
        Assert.Equal(21, all.TotalCount);
        Assert.All(all.Items, v => Assert.True(v.IsSampleData));

        var suvs = await (await client.GetAsync("/api/vehicles?category=suv&minSeats=7", Ct)).ReadAsync<PagedResult<VehicleSummaryDto>>();
        Assert.All(suvs.Items, v => { Assert.Equal("suv", v.CategorySlug); Assert.True(v.Seats >= 7); });

        var detail = await (await client.GetAsync($"/api/vehicles/{all.Items[0].Id}", Ct)).ReadAsync<VehicleDetailDto>();
        Assert.NotEmpty(detail.Media);
    }

    // ------------------------------------------------------------------ authentication and authorisation

    [Fact]
    public async Task Protected_endpoints_require_a_token_and_admin_endpoints_require_the_admin_role()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/bookings/mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/analytics/dashboard", Ct)).StatusCode);

        var customer = await factory.CustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/analytics/dashboard", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/customers", Ct)).StatusCode);

        var admin = await factory.AdminAsync();
        var dashboard = await (await admin.GetAsync("/api/admin/analytics/dashboard", Ct)).ReadAsync<DashboardDto>();
        Assert.Equal(21, dashboard.TotalVehicles);
        Assert.True(dashboard.ContainsSampleData);
    }

    [Fact]
    public async Task Registration_validates_input_and_wrong_passwords_get_one_generic_message()
    {
        var client = factory.CreateClient();

        var weak = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("A", "B", "not-an-email", "short", null), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var problem = await weak.Content.ReadAsStringAsync(Ct);
        Assert.Contains("email", problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password", problem, StringComparison.OrdinalIgnoreCase);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.CustomerEmail, "Wrong-Password-1"), Json, Ct);
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "Wrong-Password-1"), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(wrongBody.GetProperty("detail").GetString(), unknownBody.GetProperty("detail").GetString());
        Assert.DoesNotContain("stack", wrongBody.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_deactivated_customer_is_locked_out_immediately()
    {
        var (client, email) = await factory.NewCustomerAsync("Deactivated");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me", Ct)).StatusCode);

        var admin = await factory.AdminAsync();
        var found = await (await admin.GetAsync($"/api/admin/customers?q={Uri.EscapeDataString(email)}", Ct)).ReadAsync<PagedResult<AdminCustomerDto>>();
        var response = await admin.PatchAsJsonAsync($"/api/admin/customers/{found.Items.Single().Id}/status", new UpdateCustomerStatusRequest(false), Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The same, still-unexpired token is now rejected.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", Ct)).StatusCode);
    }

    // ------------------------------------------------------------------ booking workflow

    private async Task<(Guid VehicleId, int LocationId, DateTimeOffset Pickup, DateTimeOffset Return)> FreeSlotAsync(HttpClient client, int startDay)
    {
        var pickup = LocalDay(startDay, 9);
        var ret = LocalDay(startDay + 3, 9);
        var url = $"/api/vehicles?availableOnly=true&pickupAt={Uri.EscapeDataString(pickup.ToString("O"))}&returnAt={Uri.EscapeDataString(ret.ToString("O"))}&pageSize=50";
        var free = await (await client.GetAsync(url, Ct)).ReadAsync<PagedResult<VehicleSummaryDto>>();
        var locations = await (await client.GetAsync("/api/locations", Ct)).ReadAsync<List<LocationDto>>();
        Assert.NotEmpty(free.Items);
        return (free.Items[0].Id, locations[0].Id, pickup, ret);
    }

    [Fact]
    public async Task Customer_books_pays_and_sees_the_booking_and_nobody_else_can()
    {
        var (client, _) = await factory.NewCustomerAsync("Booker");
        var (vehicleId, locationId, pickup, ret) = await FreeSlotAsync(client, 60);

        var quote = await (await client.PostAsJsonAsync("/api/bookings/quote", new QuoteRequest(vehicleId, pickup, ret, locationId, locationId), Json, Ct)).ReadAsync<QuoteDto>();
        Assert.True(quote.IsAvailable);

        var created = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(vehicleId, pickup, ret, locationId, locationId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = await created.ReadAsync<BookingDetailDto>();
        Assert.Equal(quote.Total, booking.Summary.TotalAmount);   // the server's price, not the client's
        Assert.Equal("Pending", booking.Summary.Status);

        var pay = new HttpRequestMessage(HttpMethod.Post, $"/api/bookings/{booking.Summary.Reference}/pay") { Content = JsonContent.Create(new SimulatedPaymentRequest(), options: Json) };
        pay.Headers.Add("Idempotency-Key", $"it-{Guid.NewGuid():N}");
        var paid = await (await client.SendAsync(pay, Ct)).ReadAsync<BookingDetailDto>();
        Assert.Equal("Confirmed", paid.Summary.Status);
        Assert.All(paid.Payments, p => Assert.True(p.IsSimulated));

        var mine = await (await client.GetAsync("/api/bookings/mine", Ct)).ReadAsync<List<BookingSummaryDto>>();
        Assert.Contains(mine, b => b.Reference == booking.Summary.Reference);

        var (stranger, _) = await factory.NewCustomerAsync("Stranger");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/bookings/{booking.Summary.Reference}", Ct)).StatusCode);
        var steal = await stranger.PostAsJsonAsync($"/api/bookings/{booking.Summary.Reference}/cancel", new CancelBookingRequest("not mine"), Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
    }

    [Fact]
    public async Task A_second_customer_cannot_double_book_the_same_vehicle()
    {
        var (first, _) = await factory.NewCustomerAsync("First");
        var (second, _) = await factory.NewCustomerAsync("Second");
        var (vehicleId, locationId, pickup, ret) = await FreeSlotAsync(first, 90);

        var ok = await first.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(vehicleId, pickup, ret, locationId, locationId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var clash = await second.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(vehicleId, pickup.AddDays(1), ret.AddDays(1), locationId, locationId), Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Return_before_pickup_is_a_validation_error()
    {
        var (client, _) = await factory.NewCustomerAsync("Dates");
        var (vehicleId, locationId, pickup, _) = await FreeSlotAsync(client, 120);

        var response = await client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(vehicleId, pickup, pickup.AddHours(-3), locationId, locationId), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------------ recommendations, reviews, analytics

    [Fact]
    public async Task Recommendations_only_return_available_vehicles_with_explanations()
    {
        var client = factory.CreateClient();
        var request = new RecommendationRequest { PickupAt = LocalDay(30, 10), ReturnAt = LocalDay(35, 10), Passengers = 4, MaxDailyBudget = 1200m };

        var result = await (await client.PostAsJsonAsync("/api/recommendations", request, Json, Ct)).ReadAsync<RecommendationResponseDto>();

        Assert.NotEmpty(result.Matches);
        Assert.All(result.Matches, m =>
        {
            Assert.True(m.IsAvailable);
            Assert.True(m.Vehicle.Seats >= 4);
            Assert.True(m.EffectiveDailyRate <= 1200m);
            Assert.NotEmpty(m.Reasons);
        });

        var interpreted = await (await client.PostAsJsonAsync("/api/recommendations/interpret",
            new InterpretRequest("A comfortable car for four adults for a five-day trip, no more than R600 per day"), Json, Ct)).ReadAsync<InterpretedPreferencesDto>();
        Assert.Equal(4, interpreted.Passengers);
        Assert.Equal(600m, interpreted.MaxDailyBudget);
        Assert.Equal("rules", interpreted.Source);
    }

    [Fact]
    public async Task The_demo_customer_can_review_an_eligible_rental_once()
    {
        var client = await factory.CustomerAsync();
        var past = await (await client.GetAsync("/api/bookings/mine?scope=past", Ct)).ReadAsync<List<BookingSummaryDto>>();
        var eligible = past.FirstOrDefault(b => b.CanReview);
        Assert.NotNull(eligible);

        var review = new ReviewInput(4, 4, 5, 4, 4, "Smooth rental", "Clean car, easy pickup and a comfortable drive.");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/bookings/{eligible!.Reference}/review", review, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/bookings/{eligible.Reference}/review", review, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_analytics_are_computed_and_exportable()
    {
        var admin = await factory.AdminAsync();

        var utilisation = await (await admin.GetAsync("/api/admin/analytics/utilisation", Ct)).ReadAsync<UtilisationDto>();
        Assert.InRange(utilisation.FleetUtilisation, 0.0, 1.0);
        Assert.NotEmpty(utilisation.Vehicles);

        var insights = await (await admin.GetAsync("/api/admin/analytics/insights", Ct)).ReadAsync<InsightsDto>();
        Assert.All(insights.Insights, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Evidence));
            Assert.False(string.IsNullOrWhiteSpace(i.Limitations));
        });

        var csv = await admin.GetAsync("/api/admin/bookings/export", Ct);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/analytics/export/utilisation", Ct)).StatusCode);
    }

    // ------------------------------------------------------------------ data integrity

    [Fact]
    public async Task Seeded_sample_history_obeys_the_booking_rules()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartDriveDbContext>();
        var policy = scope.ServiceProvider.GetRequiredService<RentalPolicy>();
        var blocking = await db.Bookings.AsNoTracking()
            .Where(b => b.IsSampleData && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Active || b.Status == BookingStatus.Completed))
            .Select(b => new { b.VehicleId, b.PickupAt, b.ReturnAt })
            .ToListAsync(Ct);

        Assert.NotEmpty(blocking);
        foreach (var vehicle in blocking.GroupBy(b => b.VehicleId))
        {
            var ordered = vehicle.OrderBy(b => b.PickupAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i].PickupAt >= ordered[i - 1].ReturnAt + policy.Turnaround,
                    $"Overlap for vehicle {vehicle.Key}: {ordered[i - 1].ReturnAt:O} → {ordered[i].PickupAt:O}");
            }
        }

        Assert.Equal(0, await db.Reviews.CountAsync(r => r.IsSampleData && r.Booking!.Status != BookingStatus.Completed, Ct));
    }
}
