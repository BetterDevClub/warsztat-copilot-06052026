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
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.Staff.Update;

/// <summary>Updates mutable fields on a staff member. Owner only.</summary>
public static class UpdateStaff
{
    /// <summary>Request body.</summary>
    public sealed record Command(string DisplayName, string? Title, string? Email);

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
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock) { _db = db; _clock = clock; }

        /// <summary>Loads, updates, saves.</summary>
        public async Task<Result> HandleAsync(Guid id, Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            var staff = await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
            if (staff is null)
            {
                return Result.Failure(StaffErrors.NotFound);
            }
            var update = staff.Update(command.DisplayName, command.Title, command.Email, _clock.GetUtcNow());
            if (update.IsFailure) return update;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
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
            app.MapPut("/staff/{id:guid}", async (Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("Staff.Update")
                .WithTags("Staff")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
