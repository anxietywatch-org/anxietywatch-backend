using AnxietyWatch.Application.Features.Support;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoSupportTicketRepository(MongoContext context) : ISupportTicketRepository
{
    private readonly IMongoCollection<BsonDocument> tickets =
        context.Database.GetCollection<BsonDocument>("support_tickets");

    public Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default) =>
        tickets.InsertOneAsync(Map(ticket), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<SupportTicket>> GetByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var documents = await tickets.Find(Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()))
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<SupportTicket?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var document = await tickets.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    private static BsonDocument Map(SupportTicket ticket) => new()
    {
        ["_id"] = ticket.Id.ToString(),
        ["userId"] = ticket.UserId.ToString(),
        ["userEmail"] = ticket.UserEmail,
        ["subject"] = ticket.Subject,
        ["category"] = ticket.Category,
        ["priority"] = ticket.Priority,
        ["message"] = ticket.Message,
        ["status"] = ticket.Status,
        ["createdAt"] = ticket.CreatedAt.ToString("O")
    };

    private static SupportTicket Map(BsonDocument document) => new(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        document.GetValue("userEmail", string.Empty).AsString,
        document["subject"].AsString,
        document["category"].AsString,
        document["priority"].AsString,
        document["message"].AsString,
        document["status"].AsString,
        DateTimeOffset.Parse(document["createdAt"].AsString));
}
