namespace AnxietyWatch.Domain.Common;

public abstract class Entity(Guid id)
{
    public Guid Id { get; protected init; } = id;
}
