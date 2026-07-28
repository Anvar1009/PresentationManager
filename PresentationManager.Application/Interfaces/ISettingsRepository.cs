using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface ISettingsRepository
{
    /// <summary>Returns the single settings row, creating it with defaults if it does not exist yet.</summary>
    Task<AppSettings> GetAsync(CancellationToken ct = default);

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
