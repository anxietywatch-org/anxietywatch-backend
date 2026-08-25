using System.Text.Json;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Wearables;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoPatientEventRepository(MongoContext context) : IPatientEventRepository
{
    private readonly IMongoDatabase database = context.Database;

    public async Task<IReadOnlyList<PatientEventRecord>> GetAsync(
        Guid patientId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = new List<PatientEventRecord>();
        await ReadAsync<SosTriggerRequest>("sos_events", "TriggeredAt", patientId, limit, (value, document) =>
            records.Add(new(patientId, value.EventId, "SOS", value.TriggeredAt, "TRIGGERED")), cancellationToken);
        await ReadAsync<SosCancelRequest>("sos_cancellations", "CancelledAt", patientId, limit, (value, document) =>
            records.Add(new(patientId, value.EventId, "SOS_CANCELLATION", value.CancelledAt, "CANCELLED")), cancellationToken);
        await ReadAsync<SuspectedEventRequest>("suspected_events", "DetectedAt", patientId, limit, (value, document) =>
            records.Add(new(patientId, value.EventId, "SUSPECTED_EVENT", value.DetectedAt, value.State)), cancellationToken);
        await ReadAsync<EventDecisionRequest>("event_decisions", "RespondedAt", patientId, limit, (value, document) =>
            records.Add(new(patientId, value.EventId, "EVENT_DECISION", value.RespondedAt, value.Response)), cancellationToken);

        await ReadRelatedAsync<SuspectedEventRequest>("suspected_events", patientId,
            records.Where(record => record.Type == "EVENT_DECISION").Select(record => record.EventId),
            (value, document) => records.Add(new(patientId, value.EventId, "SUSPECTED_EVENT", value.DetectedAt, value.State)), cancellationToken);
        await ReadRelatedAsync<SosTriggerRequest>("sos_events", patientId,
            records.Where(record => record.Type == "SOS_CANCELLATION").Select(record => record.EventId),
            (value, document) => records.Add(new(patientId, value.EventId, "SOS", value.TriggeredAt, "TRIGGERED")), cancellationToken);

        return records
            .GroupBy(record => (record.Type is "SOS" or "SOS_CANCELLATION" ? "sos:" : "event:") + record.EventId)
            .Select(group => Merge(group))
            .OrderByDescending(record => record.OccurredAt)
            .ThenByDescending(record => record.EventId)
            .Take(limit)
            .ToArray();
    }

    private async Task ReadAsync<T>(
        string collectionName,
        string occurredAtField,
        Guid patientId,
        int limit,
        Action<T, BsonDocument> add,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("userId", patientId.ToString());
        var documents = await database.GetCollection<BsonDocument>(collectionName)
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending(occurredAtField).Descending("_id"))
            .Limit(limit)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            var value = JsonSerializer.Deserialize<T>(document.ToJson());
            if (value is not null)
            {
                add(value, document);
            }
        }
    }

    private static PatientEventRecord Merge(IEnumerable<PatientEventRecord> records)
    {
        var ordered = records.OrderByDescending(record => record.OccurredAt).ThenByDescending(record => record.EventId).ToArray();
        var first = ordered.FirstOrDefault(record => record.Type == "SUSPECTED_EVENT") ?? ordered[0];
        var decision = ordered.FirstOrDefault(record => record.Type == "EVENT_DECISION");
        var cancellation = ordered.FirstOrDefault(record => record.Type == "SOS_CANCELLATION");
        return cancellation is not null
            ? first with { Type = "SOS", Status = "CANCELLED", OccurredAt = cancellation.OccurredAt }
            : decision is not null && first.Type == "SUSPECTED_EVENT"
                ? first with { Status = decision.Status }
                : first;
    }

    private async Task ReadRelatedAsync<T>(
        string collectionName,
        Guid patientId,
        IEnumerable<Guid> eventIds,
        Action<T, BsonDocument> add,
        CancellationToken cancellationToken)
    {
        var ids = eventIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", patientId.ToString()),
            Builders<BsonDocument>.Filter.In("EventId", ids.Select(id => id.ToString())));
        var documents = await database.GetCollection<BsonDocument>(collectionName)
            .Find(filter)
            .Limit(ids.Length)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            var value = JsonSerializer.Deserialize<T>(document.ToJson());
            if (value is not null)
            {
                add(value, document);
            }
        }
    }
}
