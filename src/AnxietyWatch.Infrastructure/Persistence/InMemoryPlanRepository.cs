using AnxietyWatch.Domain.Plans;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryPlanRepository : IPlanRepository
{
    private static readonly IReadOnlyList<Plan> Plans =
    [
        Plan.Create(PlanType.Free, "Gratuito", 0, 0,
            ["Dashboard", "Registro de ansiedad"], ["1 token", "5 registros semanales"],
            "Usuarios que desean probar AnxietyWatch"),
        Plan.Create(PlanType.Individual, "Individual", 9.99m, 95.90m,
            ["Dashboard", "Historial ampliado", "Modo privado"], ["1 token"], "Uso personal"),
        Plan.Create(PlanType.Family, "Familiar", 14.99m, 143.90m,
            ["Dashboard familiar", "Miembros vinculados"], ["5 tokens"], "Familias"),
        Plan.Create(PlanType.Professional, "Profesional", 29.99m, 287.90m,
            ["Reportes clínicos", "Dashboard de pacientes"], ["20 tokens"],
            "Profesionales de la salud")
    ];

    public Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Plans);
}
