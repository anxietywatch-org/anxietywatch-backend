using AnxietyWatch.Domain.Common;

namespace AnxietyWatch.Domain.Episodes;

public sealed class Episode : Entity
{
    public Episode(Guid id, Guid userId, DateTimeOffset date, int intensity,
        IReadOnlyCollection<string> symptoms, string? notes)
        : base(id)
    {
        UserId = userId;
        Date = date;
        Intensity = intensity;
        Symptoms = symptoms;
        Notes = notes;
    }

    public Guid UserId { get; }
    public DateTimeOffset Date { get; }
    public int Intensity { get; }
    public IReadOnlyCollection<string> Symptoms { get; }
    public string? Notes { get; }
}
