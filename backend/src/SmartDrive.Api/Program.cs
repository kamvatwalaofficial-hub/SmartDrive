using SmartDrive.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartDrive();

var app = builder.Build();
await app.InitialiseDatabaseAsync();
app.UseSmartDrive();
app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
