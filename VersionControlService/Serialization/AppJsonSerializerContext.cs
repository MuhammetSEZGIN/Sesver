using System.Text.Json.Serialization;
using VersionControlService.Models;

namespace VersionControlService.Serialization;

[JsonSerializable(typeof(UpdateResponse))]
[JsonSerializable(typeof(PlatformInfo))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ReleaseEntity))]
[JsonSerializable(typeof(ReleaseArtifactEntity))]
[JsonSerializable(typeof(AdminReleaseDto))]
[JsonSerializable(typeof(List<AdminReleaseDto>))]
[JsonSerializable(typeof(AdminArtifactDto))]
[JsonSerializable(typeof(SaveReleaseRequest))]
[JsonSerializable(typeof(AdminIdentityDto))]
[JsonSerializable(typeof(List<string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}