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
        // Guncellemeler takip edilen varliklar uzerinden degil dogrudan SQL ile
        // yapilir. EF, SQLite'a Guid'i buyuk harfli metin olarak yazar; elle SQL
        // ile eklenmis kayitlarda metin farkli olabilecegi ve SQLite'ta metin
        // karsilastirmasi harf duyarli oldugu icin "WHERE Id = ..." hicbir satiri
        // bulamayip concurrency hatasi uretiyordu. Version alani uzerinden
        // calismak bu tuzagi tamamen ortadan kaldirir.
        if (release.IsLatest)
        {
            await _dbContext.Releases
                .Where(r => r.Version != release.Version && r.IsLatest)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(r => r.IsLatest, false),
                    cancellationToken);
        }

        // Ayni surum varsa satiri silip yeniden yaziyoruz; boylece owned
        // koleksiyonun (Artifacts) guncellenmesi de tek ve ongorulebilir bir
        // yoldan ilerler. ReleaseArtifacts'taki FK ON DELETE CASCADE tanimli.
        await _dbContext.Releases
            .Where(r => r.Version == release.Version)
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete/ExecuteUpdate change tracker'i guncellemez; onceki
        // sorgulardan kalan takip kayitlari Add'i bozmasin diye temizlenir.
        _dbContext.ChangeTracker.Clear();

        _dbContext.Releases.Add(release);
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
        // Kaydin surumu uzerinden silinir; bkz. UpsertLatestAsync'teki Guid notu.
        var version = await _dbContext.Releases
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (version == null)
        {
            return false;
        }

        var deleted = await _dbContext.Releases
            .Where(r => r.Version == version)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetLatestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.Releases
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => r.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (version == null)
        {
            return false;
        }

        await _dbContext.Releases
            .Where(r => r.IsLatest && r.Version != version)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(r => r.IsLatest, false),
                cancellationToken);

        await _dbContext.Releases
            .Where(r => r.Version == version)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(r => r.IsLatest, true),
                cancellationToken);

        return true;
    }
}
