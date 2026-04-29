using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.Staff;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.Staff.Create;

/// <summary>Creates a new staff member for the current tenant. Owner only.</summary>
public static class CreateStaff
{
    /// <summary>Request body.</summary>
    public sealed record Command(string DisplayName, string? Title, string? Email);

    /// <summary>Response.</summary>
    public sealed record Response(Guid Id);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(StaffMember.MaxDisplayNameLength);
            RuleFor(x => x.Title).MaximumLength(StaffMember.MaxTitleLength);
            RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly ICurrentTenant _tenant;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
        {
            _db = db; _tenant = tenant; _clock = clock;
        }

        /// <summary>Creates the staff member and persists it.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (_tenant.TenantId is null)
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            var tenantId = _tenant.TenantId.Value;

            var result = StaffMember.Create(Guid.NewGuid(), tenantId, command.DisplayName, command.Title, command.Email, _clock.GetUtcNow());
            if (result.IsFailure)
            {
                return Result.Failure<Response>(result.Error);
            }

            _db.Staff.Add(result.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(new Response(result.Value.Id));
        }
    }

    /// <summary>Endpoint registration.</summary>
    public sealed class Endpoint : IEndpoint
    {
        /// <inheritdoc />
        public EndpointScope Scope => EndpointScope.TenantScoped;

        /// <inheritdoc />
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            app.MapPost("/staff", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? result.ToCreatedResult($"/staff/{result.Value.Id}")
                        : result.ToHttpResult();
                })
                .WithName("Staff.Create")
                .WithTags("Staff")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status201Created);
        }
    }
}
