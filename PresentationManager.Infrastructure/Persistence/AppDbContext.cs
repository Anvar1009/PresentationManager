using Microsoft.EntityFrameworkCore;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Presentation> Presentations => Set<Presentation>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Presenter> Presenters => Set<Presenter>();

    public DbSet<AppSettings> Settings => Set<AppSettings>();

    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(b =>
        {
            b.ToTable("Projects");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired();
            b.HasIndex(p => p.Name);
        });

        modelBuilder.Entity<Presentation>(b =>
        {
            b.ToTable("Presentations");
            b.HasKey(p => p.Id);
            b.Property(p => p.FullName).IsRequired();
            b.Property(p => p.Title).IsRequired();
            b.Property(p => p.FilePath).IsRequired();
            b.HasIndex(p => p.OrderNumber);
            b.HasIndex(p => p.ProjectId);
            b.HasOne<Project>()
                .WithMany()
                .HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Presenter>(b =>
        {
            b.ToTable("Presenters");
            b.HasKey(p => p.Id);
            b.Property(p => p.FullName).IsRequired();
            b.HasIndex(p => p.TelegramChatId).IsUnique();
        });

        modelBuilder.Entity<AppSettings>(b =>
        {
            b.ToTable("Settings");
            b.HasKey(s => s.Id);
        });

        modelBuilder.Entity<HistoryEntry>(b =>
        {
            b.ToTable("History");
            b.HasKey(h => h.Id);
            b.Property(h => h.Message).IsRequired();
            b.HasIndex(h => h.PresentationId);
        });
    }
}
