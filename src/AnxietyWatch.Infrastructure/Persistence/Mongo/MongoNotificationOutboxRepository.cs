using System.Text.Json;
using AnxietyWatch.Domain.Notifications;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoNotificationOutboxRepository(MongoContext context) : INotificationOutboxRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("notification_outbox");

    public async Task EnsureAsync(IReadOnlyCollection<NotificationOutboxJob> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
        {
            try { await Collection.InsertOneAsync(Map(job), cancellationToken: cancellationToken); }
            catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey) { }
        }
    }

    public async Task<NotificationOutboxJob?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset leaseUntil, string claimedBy, CancellationToken cancellationToken = default)
    {
        var eligible = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("status", nameof(NotificationDeliveryStatus.Pending)),
                Builders<BsonDocument>.Filter.Lte("nextAttemptAt", Date(now))),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("status", nameof(NotificationDeliveryStatus.Processing)),
                Builders<BsonDocument>.Filter.Lte("leaseUntil", Date(now))));
        var update = Builders<BsonDocument>.Update
            .Set("status", nameof(NotificationDeliveryStatus.Processing)).Set("leaseUntil", Date(leaseUntil))
            .Set("claimedBy", claimedBy).Set("lastAttemptAt", Date(now)).Inc("attemptCount", 1);
        var document = await Collection.FindOneAndUpdateAsync(eligible, update,
            new FindOneAndUpdateOptions<BsonDocument>
            {
                ReturnDocument = ReturnDocument.After,
                Sort = Builders<BsonDocument>.Sort.Ascending("nextAttemptAt").Ascending("createdAt")
            }, cancellationToken);
        return document is null ? null : Map(document);
    }

    public Task MarkSentAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) => Update(id, Builders<BsonDocument>.Update.Set("status", nameof(NotificationDeliveryStatus.Sent)).Set("sentAt", Date(at)).Set("leaseUntil", BsonNull.Value).Set("claimedBy", BsonNull.Value), ct);
    public Task MarkSkippedAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) => Finish(id, NotificationDeliveryStatus.Skipped, reason, at, ct);
    public Task MarkRetryAsync(Guid id, string code, DateTimeOffset next, DateTimeOffset at, CancellationToken ct = default) => Update(id, Builders<BsonDocument>.Update.Set("status", nameof(NotificationDeliveryStatus.Pending)).Set("lastErrorCode", code).Set("nextAttemptAt", Date(next)).Set("lastAttemptAt", Date(at)).Set("leaseUntil", BsonNull.Value).Set("claimedBy", BsonNull.Value), ct);
    public Task MarkDeadLetterAsync(Guid id, string code, DateTimeOffset at, CancellationToken ct = default) => Finish(id, NotificationDeliveryStatus.DeadLetter, code, at, ct);
    public async Task<IReadOnlyList<NotificationOutboxJob>> GetAllAsync(CancellationToken ct = default) => (await Collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(ct)).Select(Map).ToArray();

    private Task Finish(Guid id, NotificationDeliveryStatus status, string code, DateTimeOffset at, CancellationToken ct) => Update(id, Builders<BsonDocument>.Update.Set("status", status.ToString()).Set("lastErrorCode", code).Set("lastAttemptAt", Date(at)).Set("leaseUntil", BsonNull.Value).Set("claimedBy", BsonNull.Value), ct);
    private async Task Update(Guid id, UpdateDefinition<BsonDocument> update, CancellationToken ct) => await Collection.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()), update, cancellationToken: ct);
    private static BsonDateTime Date(DateTimeOffset value) => new(value.UtcDateTime);
    private static BsonValue Nullable(DateTimeOffset? value) => value.HasValue ? Date(value.Value) : BsonNull.Value;
    private static BsonValue Nullable(string? value) => value is null ? BsonNull.Value : value;

    private static BsonDocument Map(NotificationOutboxJob j) => new()
    {
        ["_id"] = j.Id.ToString(), ["dedupeKey"] = j.DedupeKey, ["notificationType"] = j.NotificationType.ToString(),
        ["eventId"] = j.EventId.ToString(), ["patientId"] = j.PatientId.ToString(), ["caregiverId"] = j.CaregiverId.ToString(),
        ["deviceRegistrationId"] = j.DeviceRegistrationId.ToString(), ["payload"] = BsonDocument.Parse(JsonSerializer.Serialize(j.Payload)),
        ["status"] = j.Status.ToString(), ["attemptCount"] = j.AttemptCount, ["nextAttemptAt"] = Date(j.NextAttemptAt),
        ["leaseUntil"] = Nullable(j.LeaseUntil), ["claimedBy"] = Nullable(j.ClaimedBy), ["createdAt"] = Date(j.CreatedAt),
        ["sentAt"] = Nullable(j.SentAt), ["lastAttemptAt"] = Nullable(j.LastAttemptAt), ["lastErrorCode"] = Nullable(j.LastErrorCode)
    };

    private static NotificationOutboxJob Map(BsonDocument d) => new(
        Guid.Parse(d["_id"].AsString), d["dedupeKey"].AsString, Enum.Parse<CaregiverNotificationType>(d["notificationType"].AsString),
        Guid.Parse(d["eventId"].AsString), Guid.Parse(d["patientId"].AsString), Guid.Parse(d["caregiverId"].AsString), Guid.Parse(d["deviceRegistrationId"].AsString),
        JsonSerializer.Deserialize<NotificationPayload>(d["payload"].AsBsonDocument.ToJson())!, Enum.Parse<NotificationDeliveryStatus>(d["status"].AsString),
        d["attemptCount"].AsInt32, new DateTimeOffset(d["nextAttemptAt"].ToUniversalTime()), ReadDate(d, "leaseUntil"), ReadString(d, "claimedBy"),
        new DateTimeOffset(d["createdAt"].ToUniversalTime()), ReadDate(d, "sentAt"), ReadDate(d, "lastAttemptAt"), ReadString(d, "lastErrorCode"));
    private static DateTimeOffset? ReadDate(BsonDocument d, string key) => d.TryGetValue(key, out var v) && !v.IsBsonNull ? new DateTimeOffset(v.ToUniversalTime()) : null;
    private static string? ReadString(BsonDocument d, string key) => d.TryGetValue(key, out var v) && !v.IsBsonNull ? v.AsString : null;
}
