using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoUserRepository(MongoContext context) : IUserRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("users");

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("email", email))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        try
        {
            await Collection.InsertOneAsync(ToDocument(user), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var message = exception.Message.Contains("ux_users_email", StringComparison.Ordinal)
                ? "The email is already registered."
                : "The user already exists.";
            throw new ConflictException(message);
        }
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var versionFilter = user.Version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("version", 0),
                Builders<BsonDocument>.Filter.Exists("version", false))
            : Builders<BsonDocument>.Filter.Eq("version", user.Version);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", user.Id.ToString()),
            versionFilter);
        var update = Builders<BsonDocument>.Update
            .Set("fullName", user.FullName)
            .Set("email", user.Email)
            .Set("passwordHash", user.PasswordHash)
            .Set("planId", user.PlanId)
            .Set("emailVerified", user.EmailVerified)
            .Set("lastVerificationEmailSentAt", MongoDocument.NullableDate(user.LastVerificationEmailSentAt))
            .Set("avatarUrl", MongoDocument.NullableString(user.AvatarUrl))
            .Set("anxietyThreshold", user.AnxietyThreshold)
            .Set("pushNotifications", user.PushNotifications)
            .Set("privateMode", user.PrivateMode)
            .Set("failedLoginAttempts", user.FailedLoginAttempts)
            .Set("firstFailedLoginAt", MongoDocument.NullableDate(user.FirstFailedLoginAt))
            .Set("lockoutUntil", MongoDocument.NullableDate(user.LockoutUntil))
            .Inc("version", 1);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            throw new ConflictException("The user was modified by another request.");
        }

        user.MarkPersisted();
    }

    public async Task<bool> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("passwordHash", passwordHash)
            .Inc("version", 1);
        var result = await Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            update,
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<User?> RegisterFailedLoginAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var resetWindow = new BsonDocument("$or", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray
            {
                new BsonDocument("$ifNull", new BsonArray { "$firstFailedLoginAt", BsonNull.Value }),
                BsonNull.Value
            }),
            new BsonDocument("$lt", new BsonArray
            {
                "$firstFailedLoginAt",
                MongoDocument.Date(now.AddMinutes(-1))
            })
        });
        var stages = new[]
        {
            new BsonDocument("$set", new BsonDocument
            {
                ["firstFailedLoginAt"] = new BsonDocument("$cond", new BsonArray
                {
                    resetWindow,
                    MongoDocument.Date(now),
                    "$firstFailedLoginAt"
                }),
                ["failedLoginAttempts"] = new BsonDocument("$cond", new BsonArray
                {
                    resetWindow.DeepClone(),
                    1,
                    new BsonDocument("$add", new BsonArray
                    {
                        new BsonDocument("$ifNull", new BsonArray { "$failedLoginAttempts", 0 }),
                        1
                    })
                })
            }),
            new BsonDocument("$set", new BsonDocument
            {
                ["lockoutUntil"] = new BsonDocument("$cond", new BsonArray
                {
                    new BsonDocument("$gte", new BsonArray { "$failedLoginAttempts", 5 }),
                    MongoDocument.Date(now.AddSeconds(60)),
                    new BsonDocument("$ifNull", new BsonArray { "$lockoutUntil", BsonNull.Value })
                }),
                ["version"] = new BsonDocument("$add", new BsonArray
                {
                    new BsonDocument("$ifNull", new BsonArray { "$version", 0 }),
                    1
                })
            })
        };
        var document = await Collection.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            new PipelineUpdateDefinition<BsonDocument>(stages),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<User?> RegisterSuccessfulLoginAsync(
        Guid id,
        DateTimeOffset now,
        long expectedVersion,
        string expectedPasswordHash,
        CancellationToken cancellationToken = default)
    {
        var versionFilter = expectedVersion == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("version", 0),
                Builders<BsonDocument>.Filter.Exists("version", false))
            : Builders<BsonDocument>.Filter.Eq("version", expectedVersion);
        var lockoutFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("lockoutUntil", false),
            Builders<BsonDocument>.Filter.Eq("lockoutUntil", BsonNull.Value),
            Builders<BsonDocument>.Filter.Lte("lockoutUntil", MongoDocument.Date(now)));
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            versionFilter,
            Builders<BsonDocument>.Filter.Eq("passwordHash", expectedPasswordHash),
            lockoutFilter);
        var update = Builders<BsonDocument>.Update
            .Set("failedLoginAttempts", 0)
            .Set("firstFailedLoginAt", BsonNull.Value)
            .Set("lockoutUntil", BsonNull.Value)
            .Inc("version", 1);
        var document = await Collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        return document is null ? null : Map(document);
    }

    private static BsonDocument ToDocument(User user) => new()
    {
        ["_id"] = user.Id.ToString(),
        ["fullName"] = user.FullName,
        ["email"] = user.Email,
        ["passwordHash"] = user.PasswordHash,
        ["planId"] = user.PlanId,
        ["emailVerified"] = user.EmailVerified,
        ["lastVerificationEmailSentAt"] = MongoDocument.NullableDate(user.LastVerificationEmailSentAt),
        ["avatarUrl"] = MongoDocument.NullableString(user.AvatarUrl),
        ["anxietyThreshold"] = user.AnxietyThreshold,
        ["pushNotifications"] = user.PushNotifications,
        ["privateMode"] = user.PrivateMode,
        ["failedLoginAttempts"] = user.FailedLoginAttempts,
        ["firstFailedLoginAt"] = MongoDocument.NullableDate(user.FirstFailedLoginAt),
        ["lockoutUntil"] = MongoDocument.NullableDate(user.LockoutUntil),
        ["version"] = user.Version
    };

    private static User Map(BsonDocument document) => User.Rehydrate(
        Guid.Parse(document["_id"].AsString),
        document["fullName"].AsString,
        document["email"].AsString,
        document["passwordHash"].AsString,
        document["planId"].AsString,
        document.GetValue("emailVerified", false).ToBoolean(),
        MongoDocument.ReadNullableDate(document, "lastVerificationEmailSentAt"),
        MongoDocument.ReadNullableString(document, "avatarUrl"),
        document.GetValue("anxietyThreshold", 70).ToInt32(),
        document.GetValue("pushNotifications", true).ToBoolean(),
        document.GetValue("privateMode", false).ToBoolean(),
        document.GetValue("failedLoginAttempts", 0).ToInt32(),
        MongoDocument.ReadNullableDate(document, "firstFailedLoginAt"),
        MongoDocument.ReadNullableDate(document, "lockoutUntil"),
        document.GetValue("version", 0L).ToInt64());
}
