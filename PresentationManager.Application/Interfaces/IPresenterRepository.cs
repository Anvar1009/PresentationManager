using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresenterRepository
{
    Task<List<Presenter>> GetAllAsync(CancellationToken ct = default);

    Task<Presenter?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Presenter?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default);

    Task<Presenter> AddAsync(Presenter presenter, CancellationToken ct = default);

    /// <summary>Removes this presenter's own registration - their next /start in the bot then finds no
    /// <see cref="Presenter"/> row for that chat id and re-enters the one-time registration flow from
    /// scratch (see <c>PresentationBotHostedService.BeginAsync</c>), same as a brand new chat. Any project
    /// assignments cascade-delete with them (<c>PresenterProjectAssignment</c>); presentations they already
    /// submitted keep their own record with <c>PresenterId</c> set null (<c>Presentation</c>'s FK is
    /// SetNull, not Cascade) rather than disappearing.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
