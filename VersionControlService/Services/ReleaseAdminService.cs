using VersionControlService.Models;
using VersionControlService.Repositories;

namespace VersionControlService.Services;

/// <summary>
/// Admin panelinin surum yonetimi islemleri. Daha once elle SQL ile yapilan
/// insert/update islerinin yerini alir.
/// </summary>
public class ReleaseAdminService
{
    private readonly IReleaseRepository _repository;
    private readonly ISet<string> _configuredTargets;
    private readonly ILogger<ReleaseAdminService> _logger;

    public ReleaseAdminService(
        IReleaseRepository repository,
        ISet<string> configuredTargets,
        ILogger<ReleaseAdminService> logger)
    {
        _repository = repository;
        _configuredTargets = configuredTargets;
        _logger = logger;
    }

    public async Task<List<AdminReleaseDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var releases = await _repository.GetAllAsync(cancellationToken);

        return releases
            .OrderByDescending(r => r.PubDate)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<AdminReleaseDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var release = await _repository.GetByIdAsync(id, cancellationToken);
        return release == null ? null : MapToDto(release);
    }

    /// <summary>
    /// Istegi dogrular. Gecerliyse null, degilse hata mesaji doner.
    /// </summary>
    public string? Validate(SaveReleaseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Version) || !Version.TryParse(request.Version, out _))
        {
            return "Gecersiz surum formati. Ornek: 1.4.0";
        }

        if (request.Artifacts.Count == 0)
        {
            return "En az bir platform dosyasi gerekli";
        }

        foreach (var artifact in request.Artifacts)
        {
            if (!_configuredTargets.Contains(artifact.Target))
            {
                return $"Bilinmeyen platform: {artifact.Target}";
            }

            if (string.IsNullOrWhiteSpace(artifact.Signature))
            {
                return $"{artifact.Target} icin imza bos olamaz";
            }

            if (
                !Uri.TryCreate(artifact.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            )
            {
                return $"{artifact.Target} icin gecerli bir http(s) indirme adresi gerekli";
            }
        }

        var duplicateTarget = request.Artifacts
            .GroupBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicateTarget != null)
        {
            return $"Ayni platform birden fazla kez verilmis: {duplicateTarget.Key}";
        }

        return null;
    }

    /// <summary>
    /// Surumu olusturur veya ayni versiyon varsa gunceller.
    /// </summary>
    public async Task<AdminReleaseDto> SaveAsync(
        SaveReleaseRequest request,
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        var entity = new ReleaseEntity
        {
            Id = Guid.NewGuid(),
            Version = request.Version.Trim(),
            Notes = request.Notes,
            PubDate = request.PubDate ?? DateTime.UtcNow,
            IsLatest = request.IsLatest,
            Artifacts = request.Artifacts
                .Select(a => new ReleaseArtifactEntity
                {
                    Target = a.Target,
                    Signature = a.Signature.Trim(),
                    Url = a.Url.Trim()
                })
                .ToList()
        };

        await _repository.UpsertLatestAsync(entity, cancellationToken);

        _logger.LogInformation(
            "Surum kaydedildi: {Version} (isLatest={IsLatest}) - {Actor}",
            entity.Version,
            entity.IsLatest,
            actorUserName);

        // Upsert mevcut kaydi guncelleyebilecegi icin kanonik hali geri okunur.
        var saved = await _repository.GetByVersionAsync(entity.Version, cancellationToken);
        return saved == null ? MapToDto(entity) : MapToDto(saved);
    }

    public async Task<bool> DeleteAsync(Guid id, string actorUserName, CancellationToken cancellationToken = default)
    {
        var deleted = await _repository.DeleteAsync(id, cancellationToken);

        if (deleted)
        {
            _logger.LogInformation("Surum silindi: {Id} - {Actor}", id, actorUserName);
        }

        return deleted;
    }

    public async Task<bool> SetLatestAsync(Guid id, string actorUserName, CancellationToken cancellationToken = default)
    {
        var updated = await _repository.SetLatestAsync(id, cancellationToken);

        if (updated)
        {
            _logger.LogInformation("Yayindaki surum degistirildi: {Id} - {Actor}", id, actorUserName);
        }

        return updated;
    }

    public List<string> GetTargets() => _configuredTargets.OrderBy(t => t).ToList();

    private static AdminReleaseDto MapToDto(ReleaseEntity release) => new()
    {
        Id = release.Id,
        Version = release.Version,
        Notes = release.Notes,
        PubDate = release.PubDate,
        IsLatest = release.IsLatest,
        Artifacts = release.Artifacts
            .Select(a => new AdminArtifactDto
            {
                Target = a.Target,
                Signature = a.Signature,
                Url = a.Url
            })
            .ToList()
    };
}
