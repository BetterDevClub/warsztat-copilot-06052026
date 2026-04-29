using BookSlot.Domain.Abstractions;
using BookSlot.Domain.Primitives;
using BookSlot.Domain.Services;
using BookSlot.Domain.ValueObjects;
using BookSlot.Features.Shared.Endpoints;
using BookSlot.Features.Shared.Filters;
using BookSlot.Features.Shared.Http;
using BookSlot.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BookSlot.Features.ServiceTypes.Create;

/// <summary>Creates a new service type for the current tenant. Owner only.</summary>
public static class CreateServiceType
{
    /// <summary>Request body.</summary>
    public sealed record Command(
        string Name,
        string Slug,
        int DurationMinutes,
        int BufferBeforeMinutes,
        int BufferAfterMinutes,
        decimal Price,
        string Currency,
        string? Description);

    /// <summary>Response payload returned on successful creation.</summary>
    public sealed record Response(Guid Id, string Slug);

    /// <summary>Input-level validation. Deeper domain rules live in <see cref="ServiceType"/>.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(ServiceType.MaxNameLength);
            RuleFor(x => x.Slug).NotEmpty().MaximumLength(Slug.MaxLength);
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
        private readonly ICurrentTenant _tenant;
        private readonly TimeProvider _clock;

        /// <summary>Creates a new handler.</summary>
        public Handler(AppDbContext db, ICurrentTenant tenant, TimeProvider clock)
        {
            _db = db;
            _tenant = tenant;
            _clock = clock;
        }

        /// <summary>Creates the aggregate and persists it.</summary>
        public async Task<Result<Response>> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (_tenant.TenantId is null)
                return Result.Failure<Response>(Error.Unauthorized("Tenant.Unresolved", "Current tenant could not be resolved."));
            var tenantId = _tenant.TenantId.Value;

            var slugResult = Slug.Create(command.Slug);
            if (slugResult.IsFailure)
            {
                return Result.Failure<Response>(slugResult.Error);
            }

            var slug = slugResult.Value.Value;

            var exists = await _db.ServiceTypes
                .IgnoreQueryFilters()
                .AnyAsync(s => s.TenantId == tenantId && s.Slug == slug, cancellationToken)
                .ConfigureAwait(false);

            if (exists)
            {
                return Result.Failure<Response>(ServiceTypeErrors.SlugTaken);
            }

            var createResult = ServiceType.Create(
                Guid.NewGuid(),
                tenantId,
                command.Name,
                slugResult.Value,
                command.DurationMinutes,
                command.BufferBeforeMinutes,
                command.BufferAfterMinutes,
                command.Price,
                command.Currency,
                command.Description,
                _clock.GetUtcNow());

            if (createResult.IsFailure)
            {
                return Result.Failure<Response>(createResult.Error);
            }

            _db.ServiceTypes.Add(createResult.Value);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(new Response(createResult.Value.Id, createResult.Value.Slug));
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

            app.MapPost("/service-types", async (Command command, Handler handler, CancellationToken ct) =>
                {
                    var result = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return result.IsSuccess
                        ? result.ToCreatedResult($"/service-types/{result.Value.Id}")
                        : result.ToHttpResult();
                })
                .WithName("ServiceTypes.Create")
                .WithTags("ServiceTypes")
                .WithValidation<Command>()
                .RequireAuthorization("RequireOwner")
                .Produces<Response>(StatusCodes.Status201Created);
        }
    }
}
