using AnxietyWatch.Domain.Caregivers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoCaregiverRelationshipAuditRepository(MongoContext context)
    : ICaregiverRelationshipAuditRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("caregiver_relationship_audit");

    public Task AppendAsync(CaregiverRelationshipAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        Collection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = auditEvent.AuditId.ToString(),
            ["patientId"] = auditEvent.PatientId.ToString(),
            ["caregiverId"] = auditEvent.CaregiverId.ToString(),
            ["sourceTokenId"] = auditEvent.SourceTokenId.ToString(),
            ["action"] = auditEvent.Action.ToString(),
            ["occurredAt"] = new BsonDateTime(auditEvent.OccurredAt.UtcDateTime)
        }, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<CaregiverRelationshipAuditEvent>> GetAsync(
        Guid? patientId = null,
        Guid? caregiverId = null,
        CancellationToken cancellationToken = default)
    {
        var filters = new List<FilterDefinition<BsonDocument>>();
        if (patientId.HasValue)
            filters.Add(Builders<BsonDocument>.Filter.Eq("patientId", patientId.Value.ToString()));
        if (caregiverId.HasValue)
            filters.Add(Builders<BsonDocument>.Filter.Eq("caregiverId", caregiverId.Value.ToString()));

        var filter = filters.Count == 0
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.And(filters);
        var documents = await Collection.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("occurredAt").Ascending("_id"))
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    private static CaregiverRelationshipAuditEvent Map(BsonDocument document) =>
        new(
            Guid.Parse(document["_id"].AsString),
            Guid.Parse(document["patientId"].AsString),
            Guid.Parse(document["caregiverId"].AsString),
            Guid.Parse(document["sourceTokenId"].AsString),
            Enum.Parse<CaregiverRelationshipAuditAction>(document["action"].AsString),
            new DateTimeOffset(document["occurredAt"].ToUniversalTime()));
}
