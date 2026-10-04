using System.Text.Json.Serialization;

namespace Eryri.Extensions.Caching.FileSystem.Models;

[JsonSerializable(typeof(Mutation))]
[JsonSerializable(typeof(Metadata))]
[JsonSerializable(typeof(ICollection<Metadata>))]
internal partial class JsonContext : JsonSerializerContext
{
}
