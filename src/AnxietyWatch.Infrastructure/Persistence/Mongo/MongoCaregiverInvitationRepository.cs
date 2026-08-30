using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Caregivers;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoCaregiverInvitationRepository(MongoContext context) : ICaregiverInvitationRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("caregiver_invitations");

    public async Task AddAsync(CaregiverInvitation invitation, CancellationToken cancellationToken = default)
    {
        try { await Collection.InsertOneAsync(Map(invitation), cancellationToken: cancellationToken); }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey) { throw new ConflictException("The invitation code already exists."); }
    }

    public async Task<CaregiverInvitation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("code", code)).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<CaregiverInvitation?> TryAcceptAsync(Guid id, Guid caregiverId, DateTimeOffset acceptedAt, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("status", CaregiverInvitationStatus.Pending.ToString()));
        var update = Builders<BsonDocument>.Update.Set("status", CaregiverInvitationStatus.Accepted.ToString())
            .Set("acceptedByCaregiverId", caregiverId.ToString()).Set("acceptedAt", acceptedAt.UtcDateTime);
        var document = await Collection.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<bool> TryDeleteAsync(Guid id, Guid issuerId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()), Builders<BsonDocument>.Filter.Eq("issuedByUserId", issuerId.ToString()), Builders<BsonDocument>.Filter.Eq("status", CaregiverInvitationStatus.Pending.ToString()));
        var result = await Collection.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Set("status", CaregiverInvitationStatus.Deleted.ToString()), cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    private static BsonDocument Map(CaregiverInvitation x) => new()
    {
        ["_id"] = x.Id.ToString(), ["issuedByUserId"] = x.IssuedByUserId.ToString(), ["targetPatientId"] = x.TargetPatientId.ToString(),
        ["code"] = x.Code, ["expiresAt"] = x.ExpiresAt.UtcDateTime, ["status"] = x.Status.ToString(),
        ["acceptedByCaregiverId"] = x.AcceptedByCaregiverId.HasValue ? new BsonString(x.AcceptedByCaregiverId.Value.ToString()) : BsonNull.Value,
        ["acceptedAt"] = x.AcceptedAt.HasValue ? new BsonDateTime(x.AcceptedAt.Value.UtcDateTime) : BsonNull.Value
    };

    private static CaregiverInvitation Map(BsonDocument d) => CaregiverInvitation.Restore(
        Guid.Parse(d["_id"].AsString), Guid.Parse(d["issuedByUserId"].AsString), Guid.Parse(d["targetPatientId"].AsString),
        d["code"].AsString, new DateTimeOffset(d["expiresAt"].ToUniversalTime()), Enum.Parse<CaregiverInvitationStatus>(d["status"].AsString),
        d.TryGetValue("acceptedByCaregiverId", out var c) && !c.IsBsonNull ? Guid.Parse(c.AsString) : null,
        d.TryGetValue("acceptedAt", out var a) && !a.IsBsonNull ? new DateTimeOffset(a.ToUniversalTime()) : null);
}
