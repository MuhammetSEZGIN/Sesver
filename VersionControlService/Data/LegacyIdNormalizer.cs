using Microsoft.EntityFrameworkCore;

namespace VersionControlService.Data;

/// <summary>
/// EF, Guid'i SQLite'a buyuk harfli metin olarak yazar ve SQLite'ta metin
/// karsilastirmasi harf duyarlidir. Bu servisin kayitlari bir donem elle SQL
/// ile eklendigi icin kucuk harfli Guid'ler olusabildi; o satirlar
/// "WHERE Id = ..." ile hic bulunamiyor, guncelleme/silme islemleri sessizce
/// sifir satir etkileyip concurrency hatasina yol aciyordu.
/// </summary>
public static class LegacyIdNormalizer
{
    public static async Task NormalizeAsync(
        VersionControlDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(*) FROM Releases WHERE Id <> UPPER(Id)";
                var affected = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken));

                if (affected == 0)
                {
                    return;
                }

                logger.LogInformation(
                    "{Count} surum kaydinin kimligi normalize ediliyor (kucuk harfli Guid).",
                    affected);
            }

            // Ana kayit ile artifact'lar ayri UPDATE'lerle guncellendigi icin
            // aradaki anlik tutarsizlikta FK ihlali olusur; denetim islem
            // boyunca kapatilir. PRAGMA transaction disinda ayarlanmalidir.
            await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF", cancellationToken);

            await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
            {
                await ExecuteAsync(
                    connection,
                    "UPDATE Releases SET Id = UPPER(Id) WHERE Id <> UPPER(Id)",
                    cancellationToken,
                    transaction);

                await ExecuteAsync(
                    connection,
                    "UPDATE ReleaseArtifacts SET ReleaseId = UPPER(ReleaseId) WHERE ReleaseId <> UPPER(ReleaseId)",
                    cancellationToken,
                    transaction);

                await transaction.CommitAsync(cancellationToken);
            }

            await ExecuteAsync(connection, "PRAGMA foreign_keys = ON", cancellationToken);

            logger.LogInformation("Surum kimlikleri normalize edildi.");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task ExecuteAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken,
        System.Data.Common.DbTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
