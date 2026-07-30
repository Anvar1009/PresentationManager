using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PresentationManager.Infrastructure.Persistence;

/// <summary>Design-time factory so `dotnet ef migrations` works without spinning up the UI's DI host.
/// The real connection string used at runtime is supplied by PresentationManager.UI's Program.cs.</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Real credentials never live in source - set ConnectionStrings__DefaultConnection in the shell
        // before running `dotnet ef` locally. The fallback below is a harmless placeholder only, matching
        // whatever a fresh docker-compose/local Postgres install would default to.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=presentationmanager;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }
}
