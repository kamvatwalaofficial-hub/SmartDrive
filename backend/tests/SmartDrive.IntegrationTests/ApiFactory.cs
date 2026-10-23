using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using SmartDrive.Application.Contracts;

namespace SmartDrive.IntegrationTests;

/// <summary>
/// Runs the real API against a temporary SQLite database seeded with the full sample data set.
/// One instance is shared by the tests in a class.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@smartdrive.example";
    public const string AdminPassword = "Integration-Admin-1";
    public const string CustomerEmail = "customer@smartdrive.example";
    public const string CustomerPassword = "Integration-Demo-1";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"smartdrive-it-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:SmartDrive", $"Data Source={databasePath};Pooling=False;Default Timeout=30");
        builder.UseSetting("Api:ExposeDocumentation", "true");
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Seed:SampleData", "true");
        builder.UseSetting("Seed:HistoryMonths", "6");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:CustomerEmail", CustomerEmail);
        builder.UseSetting("Seed:CustomerPassword", CustomerPassword);
        builder.UseSetting("RateLimiting:AuthPerMinute", "1000");
        builder.UseSetting("RateLimiting:RecommendationsPerMinute", "1000");
        builder.UseSetting("RateLimiting:PaymentsPerMinute", "1000");
    }

    public async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), Json);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public Task<HttpClient> AdminAsync() => SignedInAsync(AdminEmail, AdminPassword);

    public Task<HttpClient> CustomerAsync() => SignedInAsync(CustomerEmail, CustomerPassword);

    /// <summary>Registers a brand-new customer and returns a signed-in client.</summary>
    public async Task<(HttpClient Client, string Email)> NewCustomerAsync(string first = "Ayanda")
    {
        var client = CreateClient();
        var email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(first, "Tester", email, "Integration-Pass-9", null), Json);
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, email);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(databasePath); } catch (IOException) { /* best effort */ }
    }
}

public static class HttpExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, ApiFactory.Json)!;
    }
}
