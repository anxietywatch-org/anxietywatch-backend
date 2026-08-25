using AnxietyWatch.Application.Common;
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
            throw new ConflictException("The email is already registered.");
        }
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", user.Id.ToString()),
            VersionFilter(user.Version));
        var update = Builders<BsonDocument>.Update
            .Set("fullName", user.FullName)
            .Set("email", user.Email.ToLowerInvariant())
            .Set("passwordHash", user.PasswordHash)
            .Set("planId", user.PlanId)
            .Set("role", user.Role)
            .Set("emailVerified", user.EmailVerified)
            .Set("lastVerificationEmailSentAt", NullableDate(user.LastVerificationEmailSentAt))
            .Set("avatarUrl", NullableString(user.AvatarUrl))
            .Set("anxietyThreshold", user.AnxietyThreshold)
            .Set("pushNotifications", user.PushNotifications)
            .Set("privateMode", user.PrivateMode)
            .Set("failedLoginAttempts", user.FailedLoginAttempts)
            .Set("firstFailedLoginAt", NullableDate(user.FirstFailedLoginAt))
            .Set("lockoutUntil", NullableDate(user.LockoutUntil))
            .Set("securityVersion", user.SecurityVersion)
            .Set("allergies", NullableString(user.Allergies))
            .Set("currentMedications", NullableString(user.CurrentMedications))
            .Set("emergencyContactName", NullableString(user.EmergencyContactName))
            .Set("emergencyContactPhone", NullableString(user.EmergencyContactPhone))
            .Set("previousAnxietyDiagnosis", NullableBool(user.PreviousAnxietyDiagnosis))
            .Set("treatingProfessional", NullableString(user.TreatingProfessional))
            .Inc("version", 1);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        if (result.MatchedCount == 0)
        {
            throw new ConflictException("The user was modified by another request.");
        }

        user.MarkPersisted();
    }

    public async Task<bool> UpdatePlanAsync(Guid id, string planId, CancellationToken cancellationToken = default)
    {
        var result = await Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Update.Set("planId", planId).Inc("version", 1),
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<bool> UpdatePasswordAsync(
        Guid id,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var update = Builders<BsonDocument>.Update
            .Set("passwordHash", passwordHash)
            .Inc("securityVersion", 1)
            .Inc("version", 1);
        var result = await Collection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            update,
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public async Task<User?> TryActivateCaregiverAsync(
        Guid id,
        long expectedVersion,
        string expectedEmail,
        string email,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            VersionFilter(expectedVersion),
            Builders<BsonDocument>.Filter.Eq("email", expectedEmail),
            Builders<BsonDocument>.Filter.Eq("role", "family_member"),
            Builders<BsonDocument>.Filter.Regex("email", new BsonRegularExpression("@device\\.anxietywatch\\.internal$", "i")));
        var update = Builders<BsonDocument>.Update
            .Set("email", email)
            .Set("passwordHash", passwordHash)
            .Set("emailVerified", false)
            .Inc("securityVersion", 1)
            .Inc("version", 1);
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
            throw new ConflictException("The email is already registered.");
        }
        catch (MongoCommandException exception) when (exception.Code == 11000)
        {
            throw new ConflictException("The email is already registered.");
        }
    }

    public async Task<User?> RegisterFailedLoginAsync(
        Guid id,
        DateTimeOffset now,
        string expectedPasswordHash,
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
                Date(now.AddMinutes(-1))
            })
        });
        var stages = new[]
        {
            new BsonDocument("$set", new BsonDocument
            {
                ["firstFailedLoginAt"] = new BsonDocument("$cond", new BsonArray
                {
                    resetWindow,
                    Date(now),
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
                    new BsonDocument("$max", new BsonArray
                    {
                        new BsonDocument("$ifNull", new BsonArray
                        {
                            "$lockoutUntil",
                            Date(now.AddSeconds(60))
                        }),
                        Date(now.AddSeconds(60))
                    }),
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
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
                Builders<BsonDocument>.Filter.Eq("passwordHash", expectedPasswordHash)),
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
        var lockoutFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("lockoutUntil", false),
            Builders<BsonDocument>.Filter.Eq("lockoutUntil", BsonNull.Value),
            Builders<BsonDocument>.Filter.Lte("lockoutUntil", Date(now)));
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            VersionFilter(expectedVersion),
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

    public async Task<EmailVerificationTokenState?> StoreEmailVerificationTokenAsync(
        Guid id,
        DateTimeOffset sentAt,
        string tokenHash,
        DateTimeOffset expiresAt,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            VersionFilter(expectedVersion),
            Builders<BsonDocument>.Filter.Ne("emailVerified", true));
        var update = Builders<BsonDocument>.Update
            .Set("lastVerificationEmailSentAt", Date(sentAt))
            .Set("emailVerificationTokenHash", tokenHash)
            .Set("emailVerificationTokenExpiresAt", Date(expiresAt))
            .Inc("version", 1);
        var document = await Collection.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.Before },
            cancellationToken);
        return document is null
            ? null
            : new EmailVerificationTokenState(
                ReadString(document, "emailVerificationTokenHash"),
                ReadDate(document, "emailVerificationTokenExpiresAt"),
                ReadDate(document, "lastVerificationEmailSentAt"));
    }

    public async Task<bool> ConfirmEmailAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("emailVerificationTokenHash", tokenHash),
            Builders<BsonDocument>.Filter.Gt("emailVerificationTokenExpiresAt", Date(now)),
            Builders<BsonDocument>.Filter.Ne("emailVerified", true));
        var update = Builders<BsonDocument>.Update
            .Set("emailVerified", true)
            .Unset("emailVerificationTokenHash")
            .Unset("emailVerificationTokenExpiresAt")
            .Inc("version", 1);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    public Task RollbackEmailVerificationTokenAsync(
        Guid id,
        string tokenHash,
        DateTimeOffset sentAt,
        EmailVerificationTokenState previousState,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", id.ToString()),
            Builders<BsonDocument>.Filter.Eq("emailVerificationTokenHash", tokenHash),
            Builders<BsonDocument>.Filter.Eq("lastVerificationEmailSentAt", Date(sentAt)),
            Builders<BsonDocument>.Filter.Ne("emailVerified", true));
        var update = Builders<BsonDocument>.Update.Inc("version", 1);
        if (previousState.TokenHash is not null && previousState.ExpiresAt is not null)
        {
            update = update
                .Set("emailVerificationTokenHash", previousState.TokenHash)
                .Set("emailVerificationTokenExpiresAt", Date(previousState.ExpiresAt.Value));
        }
        else
        {
            update = update
                .Unset("emailVerificationTokenHash")
                .Unset("emailVerificationTokenExpiresAt");
        }

        if (previousState.SentAt is not null)
        {
            update = update.Set("lastVerificationEmailSentAt", Date(previousState.SentAt.Value));
        }
        else
        {
            update = update.Unset("lastVerificationEmailSentAt");
        }

        return Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    private static FilterDefinition<BsonDocument> VersionFilter(long version) => version == 0
        ? Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("version", 0),
            Builders<BsonDocument>.Filter.Exists("version", false))
        : Builders<BsonDocument>.Filter.Eq("version", version);

    private static BsonDocument Map(User user)
    {
        var document = new BsonDocument
        {
            ["_id"] = user.Id.ToString(),
            ["fullName"] = user.FullName,
            ["email"] = user.Email.ToLowerInvariant(),
            ["passwordHash"] = user.PasswordHash,
            ["planId"] = user.PlanId,
            ["role"] = user.Role,
            ["emailVerified"] = user.EmailVerified,
            ["anxietyThreshold"] = user.AnxietyThreshold,
            ["pushNotifications"] = user.PushNotifications,
            ["privateMode"] = user.PrivateMode,
            ["failedLoginAttempts"] = user.FailedLoginAttempts,
            ["version"] = user.Version,
            ["securityVersion"] = user.SecurityVersion
        };

        AddOptional(document, "lastVerificationEmailSentAt", user.LastVerificationEmailSentAt);
        AddOptional(document, "avatarUrl", user.AvatarUrl);
        AddOptional(document, "firstFailedLoginAt", user.FirstFailedLoginAt);
        AddOptional(document, "lockoutUntil", user.LockoutUntil);
        AddOptional(document, "allergies", user.Allergies);
        AddOptional(document, "currentMedications", user.CurrentMedications);
        AddOptional(document, "emergencyContactName", user.EmergencyContactName);
        AddOptional(document, "emergencyContactPhone", user.EmergencyContactPhone);
        AddOptional(document, "previousAnxietyDiagnosis", user.PreviousAnxietyDiagnosis);
        AddOptional(document, "treatingProfessional", user.TreatingProfessional);
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
        ReadDate(document, "lockoutUntil"),
        document.GetValue("version", 0L).ToInt64(),
        document.GetValue("securityVersion", 0L).ToInt64(),
        document.GetValue("role", "patient").AsString,
        ReadString(document, "allergies"),
        ReadString(document, "currentMedications"),
        ReadString(document, "emergencyContactName"),
        ReadString(document, "emergencyContactPhone"),
        ReadNullableBool(document, "previousAnxietyDiagnosis"),
        ReadString(document, "treatingProfessional"),
        document.Contains("privateMode"));

    private static void AddOptional(BsonDocument document, string name, DateTimeOffset? value)
    {
        if (value is not null) document[name] = Date(value.Value);
    }

    private static void AddOptional(BsonDocument document, string name, string? value)
    {
        if (value is not null) document[name] = value;
    }

    private static void AddOptional(BsonDocument document, string name, bool? value)
    {
        if (value is not null) document[name] = value.Value;
    }

    private static BsonValue NullableDate(DateTimeOffset? value) =>
        value is null ? BsonNull.Value : Date(value.Value);

    private static BsonValue NullableString(string? value) =>
        value is null ? BsonNull.Value : new BsonString(value);

    private static BsonValue NullableBool(bool? value) =>
        value is null ? BsonNull.Value : new BsonBoolean(value.Value);

    private static BsonDateTime Date(DateTimeOffset value) => new(value.UtcDateTime);

    private static DateTimeOffset? ReadDate(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull
            ? new DateTimeOffset(value.ToUniversalTime())
            : null;

    private static string? ReadString(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? value.AsString : null;

    private static bool? ReadNullableBool(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? value.ToBoolean() : null;
}
