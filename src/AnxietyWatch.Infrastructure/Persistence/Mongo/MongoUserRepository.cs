using AnxietyWatch.Domain.Users;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("users");

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("email", email.ToLowerInvariant()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            await Collection.InsertOneAsync(Map(user), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException("The user already exists.", exception);
        }
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default) =>
        Collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", user.Id.ToString()),
            Map(user),
            cancellationToken: cancellationToken);

    private static BsonDocument Map(User user)
    {
        var document = new BsonDocument
        {
            ["_id"] = user.Id.ToString(),
            ["fullName"] = user.FullName,
            ["email"] = user.Email.ToLowerInvariant(),
            ["passwordHash"] = user.PasswordHash,
            ["planId"] = user.PlanId,
            ["emailVerified"] = user.EmailVerified,
            ["anxietyThreshold"] = user.AnxietyThreshold,
            ["pushNotifications"] = user.PushNotifications,
            ["privateMode"] = user.PrivateMode,
            ["failedLoginAttempts"] = user.FailedLoginAttempts
        };

        AddOptional(document, "lastVerificationEmailSentAt", user.LastVerificationEmailSentAt);
        AddOptional(document, "avatarUrl", user.AvatarUrl);
        AddOptional(document, "firstFailedLoginAt", user.FirstFailedLoginAt);
        AddOptional(document, "lockoutUntil", user.LockoutUntil);
        return document;
    }

    private static User Map(BsonDocument document) => User.Restore(
        Guid.Parse(document["_id"].AsString),
        document["fullName"].AsString,
        document["email"].AsString,
        document["passwordHash"].AsString,
        document["planId"].AsString,
        document.GetValue("emailVerified", false).ToBoolean(),
        ReadDate(document, "lastVerificationEmailSentAt"),
        ReadString(document, "avatarUrl"),
        document.GetValue("anxietyThreshold", 70).ToInt32(),
        document.GetValue("pushNotifications", true).ToBoolean(),
        document.GetValue("privateMode", false).ToBoolean(),
        document.GetValue("failedLoginAttempts", 0).ToInt32(),
        ReadDate(document, "firstFailedLoginAt"),
        ReadDate(document, "lockoutUntil"));

    private static void AddOptional(BsonDocument document, string name, DateTimeOffset? value)
    {
        if (value is not null)
        {
            document[name] = new BsonDateTime(value.Value.UtcDateTime);
        }
    }

    private static void AddOptional(BsonDocument document, string name, string? value)
    {
        if (value is not null)
        {
            document[name] = value;
        }
    }

    private static DateTimeOffset? ReadDate(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull
            ? new DateTimeOffset(value.ToUniversalTime())
            : null;

    private static string? ReadString(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? value.AsString : null;
}
