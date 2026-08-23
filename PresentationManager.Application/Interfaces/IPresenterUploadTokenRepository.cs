using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IPresenterUploadTokenRepository
{
    Task<PresenterUploadToken> AddAsync(PresenterUploadToken token, CancellationToken ct = default);

    Task<PresenterUploadToken?> GetByTokenAsync(string token, CancellationToken ct = default);
}
