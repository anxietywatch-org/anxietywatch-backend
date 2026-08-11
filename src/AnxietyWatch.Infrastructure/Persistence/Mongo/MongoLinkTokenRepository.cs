using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoLinkTokenRepository(MongoContext context) : ILinkTokenRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("link_tokens");

    public async Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Ne("status", Status(TokenStatus.Deleted)));
        var documents = await Collection.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("expiresAt"))
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<bool> TryAddAsync(
        LinkToken token,
        int maximum,
        CancellationToken cancellationToken = default)
    {
        var activeFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", token.UserId.ToString()),
            Builders<BsonDocument>.Filter.Eq("quotaActive", true));
        if (await Collection.CountDocumentsAsync(activeFilter, cancellationToken: cancellationToken) >= maximum)
        {
            return false;
        }

        for (var slot = 0; slot < maximum; slot++)
        {
            try
            {
                await Collection.InsertOneAsync(ToDocument(token, slot), cancellationToken: cancellationToken);
                return true;
            }
            catch (MongoWriteException exception) when (IsQuotaSlotConflict(exception))
            {
                // Try the next atomically protected quota slot.
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
                Builders<BsonDocument>.Filter.Gt("expiresAt", MongoDocument.Date(token.AcceptedAt.Value)));
            update = Builders<BsonDocument>.Update
                .Set("status", Status(TokenStatus.Accepted))
                .Set("acceptedBy", token.AcceptedBy.Value.ToString())
                .Set("acceptedAt", MongoDocument.Date(token.AcceptedAt.Value));
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

    private static BsonDocument ToDocument(LinkToken token, int quotaSlot) => new()
    {
        ["_id"] = token.Id.ToString(),
        ["userId"] = token.UserId.ToString(),
        ["code"] = token.Code,
        ["role"] = token.Role,
        ["expiresAt"] = MongoDocument.Date(token.ExpiresAt),
        ["status"] = Status(token.Status),
        ["acceptedBy"] = BsonNull.Value,
        ["acceptedAt"] = BsonNull.Value,
        ["quotaSlot"] = quotaSlot,
        ["quotaActive"] = true
    };

    private static LinkToken Map(BsonDocument document)
    {
        var statusValue = document.GetValue("status", Status(TokenStatus.Pending)).AsString;
        var status = Enum.TryParse<TokenStatus>(statusValue, true, out var parsed)
            ? parsed
            : TokenStatus.Pending;
        return LinkToken.Rehydrate(
            Guid.Parse(document["_id"].AsString),
            Guid.Parse(document["userId"].AsString),
            document["code"].AsString,
            document["role"].AsString,
            MongoDocument.ReadDate(document["expiresAt"]),
            status,
            MongoDocument.ReadNullableGuid(document, "acceptedBy"),
            MongoDocument.ReadNullableDate(document, "acceptedAt"));
    }

    private static bool IsQuotaSlotConflict(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey &&
        exception.Message.Contains("ux_link_tokens_active_slot", StringComparison.Ordinal);

    private static string Status(TokenStatus status) => status.ToString().ToLowerInvariant();
}
