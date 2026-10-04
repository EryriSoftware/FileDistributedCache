using System.Text.Json.Serialization.Metadata;

namespace Eryri.Extensions.Caching.FileSystem.Models;

internal sealed record Mutation(
    MutationType Kind,
    Metadata Value)
{
    public static JsonTypeInfo<Mutation> TypeInfo => JsonContext.Default.Mutation;
}