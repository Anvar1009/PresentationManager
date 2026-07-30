using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresenterRepository
{
    Task<Presenter?> GetByTelegramChatIdAsync(long telegramChatId, CancellationToken ct = default);

    Task<Presenter> AddAsync(Presenter presenter, CancellationToken ct = default);
}
