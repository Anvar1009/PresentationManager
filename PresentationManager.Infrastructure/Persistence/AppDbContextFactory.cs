using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PresentationManager.Infrastructure.Persistence;

/// <summary>Design-time factory so `dotnet ef migrations` works without spinning up the UI's DI host.
/// The real connection string used at runtime is supplied by PresentationManager.UI's Program.cs.</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=presentationmanager.db");
        return new AppDbContext(optionsBuilder.Options);
    }
}
