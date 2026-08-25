using Microsoft.Extensions.Logging;
using PresentationManager.Application.Interfaces;
using PresentationManager.Domain.Entities;

namespace PresentationManager.Application.Services;

/// <summary>SuperAdmin-facing CRUD over tenants (see <see cref="Organization"/>) - creating one is the first
/// step before assigning it a <see cref="Domain.Enums.UserRole.Manager"/> account (see
/// <see cref="UserService.CreateAsync"/>'s <c>organizationId</c> parameter).</summary>
public sealed class OrganizationService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(IOrganizationRepository organizationRepository, ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public Task<List<Organization>> GetAllAsync(CancellationToken ct = default) => _organizationRepository.GetAllAsync(ct);

    public Task<Organization?> GetByIdAsync(int id, CancellationToken ct = default) => _organizationRepository.GetByIdAsync(id, ct);

    public async Task<Organization> CreateAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("Tashkilot yaratishga urinish bo'sh nom bilan rad etildi.");
            throw new InvalidOperationException("Tashkilot nomi bo'sh bo'lishi mumkin emas.");
        }

        var created = await _organizationRepository.AddAsync(new Organization { Name = name.Trim() }, ct);
        _logger.LogInformation("Yangi tashkilot yaratildi: {OrganizationId} - {Name}", created.Id, created.Name);
        return created;
    }

    public async Task RenameAsync(int id, string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("Tashkilot nomini o'zgartirishga urinish bo'sh nom bilan rad etildi: {OrganizationId}", id);
            throw new InvalidOperationException("Tashkilot nomi bo'sh bo'lishi mumkin emas.");
        }

        await _organizationRepository.RenameAsync(id, name.Trim(), ct);
    }
}
