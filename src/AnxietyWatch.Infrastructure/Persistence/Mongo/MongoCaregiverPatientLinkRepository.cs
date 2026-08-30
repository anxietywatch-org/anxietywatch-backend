using AnxietyWatch.Domain.Caregivers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoCaregiverPatientLinkRepository(MongoContext context) : ICaregiverPatientLinkRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("caregiver_patient_links");
    public async Task<CaregiverPatientLink> EnsureLinkAsync(Guid caregiverId, Guid patientId, Guid? sourceInvitationId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("caregiverId", caregiverId.ToString()), Builders<BsonDocument>.Filter.Eq("patientId", patientId.ToString()));
        var update = Builders<BsonDocument>.Update.SetOnInsert("_id", Guid.NewGuid().ToString()).SetOnInsert("caregiverId", caregiverId.ToString()).SetOnInsert("patientId", patientId.ToString()).SetOnInsert("createdAt", createdAt.UtcDateTime).SetOnInsert<BsonDocument, BsonValue>("sourceInvitationId", sourceInvitationId.HasValue ? new BsonString(sourceInvitationId.Value.ToString()) : BsonNull.Value);
        try { return Map(await Collection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken)); }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey) { return Map(await Collection.Find(filter).FirstAsync(cancellationToken)); }
    }
    public async Task<bool> IsLinkedAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default) => await Collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("caregiverId", caregiverId.ToString()), Builders<BsonDocument>.Filter.Eq("patientId", patientId.ToString())), new CountOptions { Limit = 1 }, cancellationToken) == 1;
    public async Task<IReadOnlyList<CaregiverPatientLink>> ListByCaregiverAsync(Guid caregiverId, CancellationToken cancellationToken = default) => (await Collection.Find(Builders<BsonDocument>.Filter.Eq("caregiverId", caregiverId.ToString())).SortByDescending(x => x["createdAt"]).ToListAsync(cancellationToken)).Select(Map).ToArray();
    private static CaregiverPatientLink Map(BsonDocument d) => new(Guid.Parse(d["_id"].AsString), Guid.Parse(d["caregiverId"].AsString), Guid.Parse(d["patientId"].AsString), new DateTimeOffset(d["createdAt"].ToUniversalTime()), d.TryGetValue("sourceInvitationId", out var s) && !s.IsBsonNull ? Guid.Parse(s.AsString) : null);
}
