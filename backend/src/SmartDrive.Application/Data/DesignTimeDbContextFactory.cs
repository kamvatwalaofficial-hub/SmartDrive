using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartDrive.Application.Data;

/// <summary>
/// Used only by the EF Core command-line tools (<c>dotnet ef migrations add</c>) to build the model for
/// SQL Server. The connection string is a placeholder: generating a migration never connects to a database.
/// Set SMARTDRIVE_DESIGN_CONNECTION to point <c>dotnet ef database update</c> at a real server.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmartDriveDbContext>
{
    public SmartDriveDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("SMARTDRIVE_DESIGN_CONNECTION")
                         ?? "Server=localhost,1433;Database=SmartDrive;User Id=sa;Password=design-time-only;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<SmartDriveDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(SmartDriveDbContext).Assembly.FullName))
            .Options;
        return new SmartDriveDbContext(options);
    }
}
