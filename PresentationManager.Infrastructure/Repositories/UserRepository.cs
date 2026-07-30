using Microsoft.EntityFrameworkCore;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;
using PresentationManager.Infrastructure.Persistence;

namespace PresentationManager.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UserRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<User>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);
    }

    public async Task<User> AddAsync(User user, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Users.CountAsync(ct);
    }
}
