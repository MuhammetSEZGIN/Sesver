using System.Text.Json.Serialization;

namespace VersionControlService.Models;

/// <summary>
/// Admin panelinde bir surumun tam gorunumu.
/// </summary>
public record AdminReleaseDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("pubDate")]
    public DateTime PubDate { get; init; }

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; init; }

    [JsonPropertyName("artifacts")]
    public required List<AdminArtifactDto> Artifacts { get; init; }
}

public record AdminArtifactDto
{
    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}

/// <summary>
/// Surum olusturma / guncelleme istegi.
/// </summary>
public record SaveReleaseRequest
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>Bos birakilirsa sunucu saati kullanilir.</summary>
    [JsonPropertyName("pubDate")]
    public DateTime? PubDate { get; init; }

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; init; }

    [JsonPropertyName("artifacts")]
    public required List<AdminArtifactDto> Artifacts { get; init; }
}

/// <summary>
/// Panelin oturum acan kullanicinin yetkisini dogrulamasi icin.
/// </summary>
public record AdminIdentityDto
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    [JsonPropertyName("userName")]
    public required string UserName { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }
}
