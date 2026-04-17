using BookSlot.Domain.Primitives;
using BookSlot.Domain.Services;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.Update;

/// <summary>Replaces mutable fields on a service type. Slug is immutable. Owner only.</summary>
public static class UpdateServiceType
{
    /// <summary>Request body — mirrors Create minus Slug.</summary>
    public sealed record Command(
        string Name,
        int DurationMinutes,
        int BufferBeforeMinutes,
        int BufferAfterMinutes,
        decimal Price,
        string Currency,
        string? Description);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(ServiceType.MaxNameLength);
            RuleFor(x => x.DurationMinutes).InclusiveBetween(ServiceType.MinDurationMinutes, ServiceType.MaxDurationMinutes);
            RuleFor(x => x.BufferBeforeMinutes).InclusiveBetween(0, ServiceType.MaxBufferMinutes);
            RuleFor(x => x.BufferAfterMinutes).InclusiveBetween(0, ServiceType.MaxBufferMinutes);
            RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
            RuleFor(x => x.Currency).NotEmpty().Length(3);
            RuleFor(x => x.Description).MaximumLength(ServiceType.MaxDescriptionLength);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly AppDbContext _db;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, TimeProvider clock)
        {
            _db = db;
            _clock = clock;
        }

        /// <summary>Loads the aggregate, applies the update, saves.</summary>
        public async Task<Result> HandleAsync(Guid id, Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            var serviceType = await _db.ServiceTypes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
            if (serviceType is null)
            {
                return Result.Failure(ServiceTypeErrors.NotFound);
            }

            var update = serviceType.Update(
                command.Name,
                command.DurationMinutes,
                command.BufferBeforeMinutes,
                command.BufferAfterMinutes,
                command.Price,
                command.Currency,
                command.Description,
                _clock.GetUtcNow());

            if (update.IsFailure)
            {
                return update;
            }

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

            app.MapPut("/service-types/{id:guid}", async (Guid id, Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(id, command, ct).ConfigureAwait(false);
                    return result.ToHttpResult(StatusCodes.Status204NoContent);
                })
                .WithName("ServiceTypes.Update")
                .WithTags("ServiceTypes")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner");
        }
    }
}
