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

    public DbSet<User> Users => Set<User>();

    public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();

    public DbSet<Judge> Judges => Set<Judge>();

    public DbSet<Score> Scores => Set<Score>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(b =>
        {
            b.ToTable("Projects");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).IsRequired();
            b.HasIndex(p => p.Name);
            // Defaults only matter for backfilling rows that existed before this column was added -
            // ProjectService.CreateAsync always supplies real values for every new project going forward.
            b.Property(p => p.EventStartDate).HasDefaultValueSql("CURRENT_DATE");
            b.Property(p => p.EventEndDate).HasDefaultValueSql("CURRENT_DATE");
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
            b.HasIndex(p => p.PresenterId);
            b.HasOne<Presenter>()
                .WithMany()
                .HasForeignKey(p => p.PresenterId)
                .OnDelete(DeleteBehavior.SetNull);
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

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Username).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
            b.Property(u => u.FullName).IsRequired();
            b.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<EvaluationCriterion>(b =>
        {
            b.ToTable("EvaluationCriteria");
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).IsRequired();
            b.HasIndex(c => c.ProjectId);
            b.HasOne<Project>()
                .WithMany()
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Judge>(b =>
        {
            b.ToTable("Judges");
            b.HasKey(j => j.Id);
            b.Property(j => j.PhoneNumber).IsRequired();
            b.HasIndex(j => j.ProjectId);
            b.HasIndex(j => j.PhoneNumber);
            b.HasIndex(j => j.TelegramChatId);
            b.HasOne<Project>()
                .WithMany()
                .HasForeignKey(j => j.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Score>(b =>
        {
            b.ToTable("Scores");
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.PresentationId, s.JudgeId, s.CriterionId }).IsUnique();
            b.HasOne<Presentation>()
                .WithMany()
                .HasForeignKey(s => s.PresentationId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Judge>()
                .WithMany()
                .HasForeignKey(s => s.JudgeId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<EvaluationCriterion>()
                .WithMany()
                .HasForeignKey(s => s.CriterionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
