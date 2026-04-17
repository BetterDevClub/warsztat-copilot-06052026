using BookSlot.Domain.Integrations;
using BookSlot.Features.Shared.Endpoints;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BookSlot.Features.Integrations.Zoom.GenerateMeeting;

/// <summary>
/// Generates a Zoom meeting URL for the given booking window via the
/// <see cref="IMeetingLinkGenerator"/> abstraction. Phase 18 wires a mock
/// generator; the real Zoom adapter lands in Phase 22.
/// </summary>
public static class GenerateZoomMeeting
{
    /// <summary>Request body.</summary>
    public sealed record Command(string Topic, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

    /// <summary>Response body.</summary>
    public sealed record Response(string JoinUrl, string? Passcode, string ExternalMeetingId);

    /// <summary>Input validation.</summary>
    public sealed class Validator : AbstractValidator<Command>
    {
        /// <summary>Creates a new validator.</summary>
        public Validator()
        {
            RuleFor(c => c.Topic).NotEmpty().MaximumLength(200);
            RuleFor(c => c.StartUtc).NotEqual(default(DateTimeOffset));
            RuleFor(c => c.EndUtc).GreaterThan(c => c.StartUtc);
        }
    }

    /// <summary>Slice handler.</summary>
    public sealed class Handler
    {
        private readonly IEnumerable<IMeetingLinkGenerator> _generators;

        /// <summary>Creates a new handler.</summary>
        public Handler(IEnumerable<IMeetingLinkGenerator> generators) => _generators = generators;

        /// <summary>Creates the meeting via the Zoom-flavoured generator.</summary>
        public async Task<Response> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var generator = _generators.First(g => g.Provider == MeetingProvider.Zoom);
            var link = await generator.CreateMeetingAsync(
                command.Topic, command.StartUtc, command.EndUtc, cancellationToken).ConfigureAwait(false);
            return new Response(link.JoinUrl, link.Passcode, link.ExternalMeetingId);
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
            app.MapPost("/integrations/zoom/meetings", async (
                    Command command, Handler handler, CancellationToken ct) =>
                {
                    var validator = new Validator();
                    var validation = await validator.ValidateAsync(command, ct).ConfigureAwait(false);
                    if (!validation.IsValid)
                        return Results.ValidationProblem(
                            validation.Errors.GroupBy(e => e.PropertyName)
                                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

                    var response = await handler.HandleAsync(command, ct).ConfigureAwait(false);
                    return Results.Ok(response);
                })
                .WithName("Integrations.Zoom.GenerateMeeting")
                .WithTags("Integrations")
                .RequireAuthorization("RequireStaff")
                .Produces<Response>()
                .ProducesValidationProblem();
        }
    }
}
