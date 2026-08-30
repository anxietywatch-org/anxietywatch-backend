using AnxietyWatch.Domain.FamilyPlans;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoFamilyPlanPatientMembershipRepository(MongoContext context) : IFamilyPlanPatientMembershipRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("family_plan_patient_memberships");
    public async Task<FamilyPlanPatientMembership> EnsureMembershipAsync(Guid ownerUserId, Guid patientUserId, Guid? sourceTokenId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("ownerUserId", ownerUserId.ToString()), Builders<BsonDocument>.Filter.Eq("patientUserId", patientUserId.ToString()));
        var update = Builders<BsonDocument>.Update
            .SetOnInsert("_id", Guid.NewGuid().ToString())
            .SetOnInsert("ownerUserId", ownerUserId.ToString())
            .SetOnInsert("patientUserId", patientUserId.ToString())
            .SetOnInsert("createdAt", new BsonDateTime(createdAt.UtcDateTime))
            .SetOnInsert("status", FamilyPlanPatientMembershipStatus.Active.ToString());
        update = sourceTokenId.HasValue
            ? update.SetOnInsert("sourceTokenId", sourceTokenId.Value.ToString())
            : update.SetOnInsert("sourceTokenId", BsonNull.Value);
        try
        {
            return Map(await Collection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, cancellationToken));
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var existing = await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) return Map(existing);
            throw;
        }
    }
    public async Task<bool> CanManagePatientAsync(Guid ownerUserId, Guid patientUserId, CancellationToken cancellationToken = default) => await Collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("ownerUserId", ownerUserId.ToString()), Builders<BsonDocument>.Filter.Eq("patientUserId", patientUserId.ToString()), Builders<BsonDocument>.Filter.Eq("status", FamilyPlanPatientMembershipStatus.Active.ToString())), new CountOptions { Limit = 1 }, cancellationToken) == 1;
    public async Task<IReadOnlyList<FamilyPlanPatientMembership>> ListPatientsAsync(Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var documents = await Collection.Find(Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("ownerUserId", ownerUserId.ToString()), Builders<BsonDocument>.Filter.Eq("status", FamilyPlanPatientMembershipStatus.Active.ToString()))).SortByDescending(x => x["createdAt"]).ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }
    private static FamilyPlanPatientMembership Map(BsonDocument d) => new(Guid.Parse(d["_id"].AsString), Guid.Parse(d["ownerUserId"].AsString), Guid.Parse(d["patientUserId"].AsString), new DateTimeOffset(d["createdAt"].ToUniversalTime()), d.TryGetValue("sourceTokenId", out var s) && !s.IsBsonNull ? Guid.Parse(s.AsString) : null, Enum.Parse<FamilyPlanPatientMembershipStatus>(d["status"].AsString, true));
}
