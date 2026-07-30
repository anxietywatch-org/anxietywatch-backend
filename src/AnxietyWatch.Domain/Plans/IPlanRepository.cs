namespace AnxietyWatch.Domain.Plans;

public interface IPlanRepository
{
    Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken cancellationToken = default);
}
