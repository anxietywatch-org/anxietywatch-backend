using MongoDB.Bson;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

internal static class MongoDocument
{
    public static BsonValue Date(DateTimeOffset value) => new BsonDateTime(value.UtcDateTime);

    public static BsonValue NullableDate(DateTimeOffset? value) =>
        value.HasValue ? Date(value.Value) : BsonNull.Value;

    public static BsonValue NullableString(string? value) =>
        value is null ? BsonNull.Value : value;

    public static DateTimeOffset ReadDate(BsonValue value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value.AsBsonDateTime.MillisecondsSinceEpoch);

    public static DateTimeOffset? ReadNullableDate(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? ReadDate(value) : null;

    public static string? ReadNullableString(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? value.AsString : null;

    public static Guid? ReadNullableGuid(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? Guid.Parse(value.AsString) : null;
}
