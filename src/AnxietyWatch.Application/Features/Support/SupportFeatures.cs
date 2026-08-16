using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Support;

public sealed record SupportTicket(
    Guid Id,
    Guid UserId,
    string UserEmail,
    string Subject,
    string Category,
    string Priority,
    string Message,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record SupportTicketResponse(
    Guid Id,
    string Subject,
    string Category,
    string Priority,
    string Message,
    string Status,
    DateTimeOffset CreatedAt);

public interface ISupportTicketRepository
{
    Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SupportTicket>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record CreateSupportTicketCommand(
    string Subject,
    string Category,
    string Priority,
    string Message) : IRequest<SupportTicketResponse>;

public sealed class CreateSupportTicketCommandValidator : AbstractValidator<CreateSupportTicketCommand>
{
    private static readonly string[] Categories = ["general", "tokens", "billing", "technical"];
    private static readonly string[] Priorities = ["low", "normal", "high"];

    public CreateSupportTicketCommandValidator()
    {
        RuleFor(command => command.Subject).NotEmpty().Length(3, 120);
        RuleFor(command => command.Category)
            .Must(value => Categories.Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(command => command.Priority)
            .Must(value => Priorities.Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(command => command.Message).NotEmpty().Length(10, 4_000);
    }
}

public sealed class CreateSupportTicketCommandHandler(
    ICurrentUser currentUser,
    ISupportTicketRepository tickets)
    : IRequestHandler<CreateSupportTicketCommand, SupportTicketResponse>
{
    public async Task<SupportTicketResponse> Handle(
        CreateSupportTicketCommand command,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser(currentUser);
        var ticket = new SupportTicket(
            Guid.NewGuid(),
            userId,
            currentUser.Email ?? string.Empty,
            command.Subject.Trim(),
            command.Category.ToLowerInvariant(),
            command.Priority.ToLowerInvariant(),
            command.Message.Trim(),
            "open",
            DateTimeOffset.UtcNow);
        await tickets.AddAsync(ticket, cancellationToken);
        return Map(ticket);
    }

    internal static Guid RequireUser(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        return currentUser.UserId;
    }

    internal static SupportTicketResponse Map(SupportTicket ticket) => new(
        ticket.Id,
        ticket.Subject,
        ticket.Category,
        ticket.Priority,
        ticket.Message,
        ticket.Status,
        ticket.CreatedAt);
}

public sealed record GetSupportTicketsQuery : IRequest<IReadOnlyList<SupportTicketResponse>>;

public sealed class GetSupportTicketsQueryHandler(
    ICurrentUser currentUser,
    ISupportTicketRepository tickets)
    : IRequestHandler<GetSupportTicketsQuery, IReadOnlyList<SupportTicketResponse>>
{
    public async Task<IReadOnlyList<SupportTicketResponse>> Handle(
        GetSupportTicketsQuery request,
        CancellationToken cancellationToken)
    {
        var userId = CreateSupportTicketCommandHandler.RequireUser(currentUser);
        var result = await tickets.GetByUserAsync(userId, cancellationToken);
        return result.Select(CreateSupportTicketCommandHandler.Map).ToArray();
    }
}

public sealed record GetSupportTicketQuery(Guid Id) : IRequest<SupportTicketResponse>;

public sealed class GetSupportTicketQueryHandler(
    ICurrentUser currentUser,
    ISupportTicketRepository tickets)
    : IRequestHandler<GetSupportTicketQuery, SupportTicketResponse>
{
    public async Task<SupportTicketResponse> Handle(
        GetSupportTicketQuery request,
        CancellationToken cancellationToken)
    {
        var userId = CreateSupportTicketCommandHandler.RequireUser(currentUser);
        var ticket = await tickets.GetByIdAsync(request.Id, cancellationToken);
        if (ticket is null || ticket.UserId != userId)
        {
            throw new NotFoundException("Support ticket not found.");
        }

        return CreateSupportTicketCommandHandler.Map(ticket);
    }
}
