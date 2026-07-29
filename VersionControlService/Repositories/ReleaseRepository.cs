using Microsoft.EntityFrameworkCore;
using VersionControlService.Data;
using VersionControlService.Models;

namespace VersionControlService.Repositories;

/// <summary>
/// EF Core implementation of release data access.
/// </summary>
public class ReleaseRepository : IReleaseRepository
{
    private readonly VersionControlDbContext _dbContext;

    public ReleaseRepository(VersionControlDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<ReleaseEntity?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Releases
            .AsNoTracking()
            .Where(r => r.IsLatest)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReleaseEntity?> GetByVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Version == version, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertLatestAsync(ReleaseEntity release, CancellationToken cancellationToken = default)
    {
        // Find existing release with same version
        var existing = await _dbContext.Releases
            .FirstOrDefaultAsync(r => r.Version == release.Version, cancellationToken);

        // Yayindaki isareti once temizlenir: ayni SaveChanges icinde hem yeni
        // kaydi ekleyip hem de digerlerini guncellemek, owned koleksiyonun
        // yeniden olusturulmasiyla birleşince EF'i concurrency hatasina dusuruyor.
        if (release.IsLatest)
        {
            var otherReleases = await _dbContext.Releases
                .Where(r => r.Version != release.Version && r.IsLatest)
                .ToListAsync(cancellationToken);

            if (otherReleases.Count > 0)
            {
                foreach (var other in otherReleases)
                {
                    other.IsLatest = false;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        if (existing != null)
        {
            existing.Notes = release.Notes;
            existing.PubDate = release.PubDate;
            existing.IsLatest = release.IsLatest;

            // Owned koleksiyonda tek tek Clear/Add yapmak yerine referansi
            // degistiriyoruz; EF eski satirlari silip yenilerini ekler.
            existing.Artifacts = release.Artifacts
                .Select(a => new ReleaseArtifactEntity
                {
                    Target = a.Target,
                    Signature = a.Signature,
                    Url = a.Url
                })
                .ToList();
        }
        else
        {
            _dbContext.Releases.Add(release);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Releases.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ReleaseEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Releases
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ReleaseEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var release = await _dbContext.Releases
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (release == null)
        {
            return false;
        }

        _dbContext.Releases.Remove(release);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetLatestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var target = await _dbContext.Releases
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (target == null)
        {
            return false;
        }

        var previouslyLatest = await _dbContext.Releases
            .Where(r => r.IsLatest && r.Id != id)
            .ToListAsync(cancellationToken);

        foreach (var release in previouslyLatest)
        {
            release.IsLatest = false;
        }

        target.IsLatest = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
