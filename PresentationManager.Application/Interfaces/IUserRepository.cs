using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Interfaces;

public interface IUserRepository
{
    Task<List<User>> GetAllAsync(CancellationToken ct = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

    Task<User> AddAsync(User user, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);
}
