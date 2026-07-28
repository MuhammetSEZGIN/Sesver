using VersionControlService.Models;

namespace VersionControlService.Repositories;

/// <summary>
/// Repository interface for release data access.
/// </summary>
public interface IReleaseRepository
{
    /// <summary>
    /// Gets the latest available release.
    /// </summary>
    Task<ReleaseEntity?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific release by version.
    /// </summary>
    Task<ReleaseEntity?> GetByVersionAsync(string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a release and marks it as latest.
    /// </summary>
    Task UpsertLatestAsync(ReleaseEntity release, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any releases exist in the database.
    /// </summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all releases.
    /// </summary>
    Task<List<ReleaseEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific release by id.
    /// </summary>
    Task<ReleaseEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a release by id. Returns false when no release matched.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a single release as the latest one, clearing the flag on all others.
    /// </summary>
    Task<bool> SetLatestAsync(Guid id, CancellationToken cancellationToken = default);
}
