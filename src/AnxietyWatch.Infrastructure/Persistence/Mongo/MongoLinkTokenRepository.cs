using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoLinkTokenRepository(MongoContext context) : ILinkTokenRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("link_tokens");

    public async Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Ne("status", Status(TokenStatus.Deleted)));
        var documents = await Collection.Find(filter)
            .SortByDescending(document => document["expiresAt"])
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default)
    {
        var activeFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", token.UserId.ToString()),
            Builders<BsonDocument>.Filter.Ne("status", Status(TokenStatus.Deleted)));
        var activeDocuments = await Collection.Find(activeFilter)
            .Project(Builders<BsonDocument>.Projection.Include("quotaSlot"))
            .ToListAsync(cancellationToken);
        if (activeDocuments.Count >= maximum)
        {
            return false;
        }

        var occupiedSlots = activeDocuments
            .Where(document => document.TryGetValue("quotaSlot", out var value) && value.IsInt32)
            .Select(document => document["quotaSlot"].AsInt32)
            .ToHashSet();
        var availableSlots = maximum - activeDocuments.Count;
        var candidates = Enumerable.Range(0, maximum)
            .Where(slot => !occupiedSlots.Contains(slot))
            .Take(availableSlots);

        foreach (var slot in candidates)
        {
            try
            {
                await Collection.InsertOneAsync(Map(token, slot), cancellationToken: cancellationToken);
                return true;
            }
            catch (MongoWriteException exception) when (IsQuotaSlotConflict(exception))
            {
                // Another request reserved this quota slot; try the next candidate.
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConflictException("The token already exists.");
            }
        }

        return false;
    }

    public async Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<LinkToken?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("code", code))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<IReadOnlyList<LinkToken>> GetAcceptedPatientTokensAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Accepted)), Builders<BsonDocument>.Filter.Eq("role", "patient"), Builders<BsonDocument>.Filter.Exists("acceptedBy"), Builders<BsonDocument>.Filter.Ne("acceptedBy", BsonNull.Value));
        return (await Collection.Find(filter).ToListAsync(cancellationToken)).Select(Map).ToArray();
    }

    public async Task<bool> HasAcceptedCaregiverRelationshipAsync(
        Guid patientId,
        Guid caregiverId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", patientId.ToString()),
            Builders<BsonDocument>.Filter.Eq("acceptedBy", caregiverId.ToString()),
            Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Accepted)),
            Builders<BsonDocument>.Filter.Eq("role", "family_member"));

        return await Collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AcceptedCaregiverRelationship>> GetAcceptedCaregiverRelationshipsAsync(
        Guid caregiverId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("acceptedBy", caregiverId.ToString()),
            Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Accepted)),
            Builders<BsonDocument>.Filter.Eq("role", "family_member"),
            Builders<BsonDocument>.Filter.Exists("acceptedAt"),
            Builders<BsonDocument>.Filter.Ne("acceptedAt", BsonNull.Value));

        var documents = await Collection.Find(filter)
            .Project(Builders<BsonDocument>.Projection
                .Include("userId")
                .Include("role")
                .Include("acceptedAt"))
            .Sort(Builders<BsonDocument>.Sort.Ascending("userId").Ascending("acceptedAt"))
            .ToListAsync(cancellationToken);

        return documents
            .GroupBy(document => document["userId"].AsString)
            .Select(group => group.First())
            .OrderByDescending(document => document["acceptedAt"].ToUniversalTime())
            .Select(document => new AcceptedCaregiverRelationship(
                Guid.Parse(document["userId"].AsString),
                document["role"].AsString,
                new DateTimeOffset(document["acceptedAt"].ToUniversalTime())))
            .ToArray();
    }

    public async Task<LinkToken?> TryRotateAsync(
        Guid id,
        Guid ownerId,
        string expectedCode,
        string newCode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("userId", ownerId.ToString()),
            Builders<BsonDocument>.Filter.Eq("code", expectedCode),
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Pending)),
                Builders<BsonDocument>.Filter.Exists("status", false)));
        var update = Builders<BsonDocument>.Update
            .Set("code", newCode)
            .Set("expiresAt", Date(expiresAt))
            .Set("status", Status(TokenStatus.Pending))
            .Set("acceptedBy", BsonNull.Value)
            .Set("acceptedAt", BsonNull.Value);

        try
        {
            var document = await Collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken);
            return document is null ? null : Map(document);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException("The token code already exists.");
        }
    }

    public async Task<bool> TryAcceptAsync(
        Guid id,
        string expectedCode,
        Guid acceptedBy,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("code", expectedCode),
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Pending)),
                Builders<BsonDocument>.Filter.Exists("status", false)),
            Builders<BsonDocument>.Filter.Gt("expiresAt", Date(acceptedAt)));
        var update = Builders<BsonDocument>.Update
            .Set("status", Status(TokenStatus.Accepted))
            .Set("acceptedBy", acceptedBy.ToString())
            .Set("acceptedAt", Date(acceptedAt));
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<bool> TryDeleteAsync(Guid id, string expectedCode, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("code", expectedCode),
            Builders<BsonDocument>.Filter.Ne("status", Status(TokenStatus.Accepted)));
        var update = Builders<BsonDocument>.Update
            .Set("status", Status(TokenStatus.Deleted))
            .Set("quotaActive", false);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Accepted)));
        var update = Builders<BsonDocument>.Update
            .Set("status", Status(TokenStatus.Deleted))
            .Set("quotaActive", false);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default)
    {
        var idFilter = Builders<BsonDocument>.Filter.Eq("_id", token.Id.ToString());
        FilterDefinition<BsonDocument> filter;
        UpdateDefinition<BsonDocument> update;

        if (token.Status == TokenStatus.Accepted && token.AcceptedBy.HasValue && token.AcceptedAt.HasValue)
        {
            filter = Builders<BsonDocument>.Filter.And(
                idFilter,
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("status", Status(TokenStatus.Pending)),
                    Builders<BsonDocument>.Filter.Exists("status", false)),
                Builders<BsonDocument>.Filter.Gt("expiresAt", Date(token.AcceptedAt.Value)));
            update = Builders<BsonDocument>.Update
                .Set("status", Status(TokenStatus.Accepted))
                .Set("acceptedBy", token.AcceptedBy.Value.ToString())
                .Set("acceptedAt", Date(token.AcceptedAt.Value));
        }
        else if (token.Status == TokenStatus.Deleted)
        {
            filter = Builders<BsonDocument>.Filter.And(
                idFilter,
                Builders<BsonDocument>.Filter.Ne("status", Status(TokenStatus.Accepted)));
            update = Builders<BsonDocument>.Update
                .Set("status", Status(TokenStatus.Deleted))
                .Set("quotaActive", false);
        }
        else
        {
            filter = idFilter;
            update = Builders<BsonDocument>.Update.Set("status", Status(token.Status));
        }

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            throw new ConflictException("The token state changed before the request completed.");
        }
    }

    private static BsonDocument Map(LinkToken token, int quotaSlot) => new()
    {
        ["_id"] = token.Id.ToString(),
        ["userId"] = token.UserId.ToString(),
        ["code"] = token.Code,
        ["role"] = token.Role,
        ["expiresAt"] = Date(token.ExpiresAt),
        ["status"] = Status(token.Status),
        ["acceptedBy"] = BsonNull.Value,
        ["acceptedAt"] = BsonNull.Value,
        ["quotaSlot"] = quotaSlot,
        ["quotaActive"] = true
    };

    private static LinkToken Map(BsonDocument document) => LinkToken.Restore(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        document["code"].AsString,
        document["role"].AsString,
        new DateTimeOffset(document["expiresAt"].ToUniversalTime()),
        Enum.Parse<TokenStatus>(document.GetValue("status", Status(TokenStatus.Pending)).AsString, true),
        document.TryGetValue("acceptedBy", out var acceptedBy) && !acceptedBy.IsBsonNull
            ? Guid.Parse(acceptedBy.AsString)
            : null,
        document.TryGetValue("acceptedAt", out var acceptedAt) && !acceptedAt.IsBsonNull
            ? new DateTimeOffset(acceptedAt.ToUniversalTime())
            : null);

    private static bool IsQuotaSlotConflict(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey &&
        exception.Message.Contains("ux_link_tokens_active_slot", StringComparison.Ordinal);

    private static string Status(TokenStatus status) => status.ToString();

    private static BsonDateTime Date(DateTimeOffset value) => new(value.UtcDateTime);
}
