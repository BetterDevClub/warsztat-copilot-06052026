using System.Text;
using BookSlot.Domain.Abstractions;
using BookSlot.Features.ApiKeys.Create;
using BookSlot.Features.ApiKeys.List;
using BookSlot.Features.ApiKeys.Revoke;
using BookSlot.Features.Auth.ConfirmEmail;
using BookSlot.Features.Auth.Login;
using BookSlot.Features.Auth.Logout;
using BookSlot.Features.Auth.Refresh;
using BookSlot.Features.Auth.RequestPasswordReset;
using BookSlot.Features.Auth.ResetPassword;
using BookSlot.Features.Shared.Emailing;
using BookSlot.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace BookSlot.Features.Shared.Auth;

/// <summary>
/// DI wire-up for JWT bearer authentication, role-based authorization policies, the
/// scoped <see cref="ICurrentUser"/> accessor, and every auth / API key slice handler.
/// Host calls <see cref="AddAuth"/> after <c>AddInfrastructure</c> (so <see cref="JwtOptions"/>
/// is already bound).
/// </summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers JWT bearer authentication, the role-based authorization policies
    /// <c>RequireOwner</c> / <c>RequireStaff</c> / <c>RequireViewer</c>, the scoped
    /// <see cref="CurrentUserAccessor"/>, the dev email sender, and every handler
    /// used by Phase 6 slices.
    /// </summary>
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserAccessor>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>()));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"Missing '{JwtOptions.SectionName}' configuration section — JWT bearer cannot be wired.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("RequireOwner", p => p.RequireRole(Roles.Owner))
            .AddPolicy("RequireStaff", p => p.RequireRole(Roles.Owner, Roles.Staff))
            .AddPolicy("RequireViewer", p => p.RequireRole(Roles.Owner, Roles.Staff, Roles.Viewer));

        services.TryAddSingleton<IEmailSender, NoOpEmailSender>();

        // Slice handlers — scoped because they consume the scoped AppDbContext.
        services.AddScoped<Login.Handler>();
        services.AddScoped<Refresh.Handler>();
        services.AddScoped<Logout.Handler>();
        services.AddScoped<ConfirmEmail.Handler>();
        services.AddScoped<RequestPasswordReset.Handler>();
        services.AddScoped<ResetPassword.Handler>();
        services.AddScoped<CreateApiKey.Handler>();
        services.AddScoped<RevokeApiKey.Handler>();
        services.AddScoped<ListApiKeys.Handler>();
        services.AddScoped<Features.Tenants.Register.RegisterTenant.Handler>();
        services.AddScoped<Features.Tenants.GetSettings.GetTenantSettings.Handler>();
        services.AddScoped<Features.Tenants.UpdateSettings.UpdateTenantSettings.Handler>();
        services.AddScoped<Features.ServiceTypes.Create.CreateServiceType.Handler>();
        services.AddScoped<Features.ServiceTypes.Update.UpdateServiceType.Handler>();
        services.AddScoped<Features.ServiceTypes.GetById.GetServiceTypeById.Handler>();
        services.AddScoped<Features.ServiceTypes.List.ListServiceTypes.Handler>();
        services.AddScoped<Features.ServiceTypes.Deactivate.DeactivateServiceType.Handler>();
        services.AddScoped<Features.Staff.Create.CreateStaff.Handler>();
        services.AddScoped<Features.Staff.Update.UpdateStaff.Handler>();
        services.AddScoped<Features.Staff.GetById.GetStaffById.Handler>();
        services.AddScoped<Features.Staff.List.ListStaff.Handler>();
        services.AddScoped<Features.Staff.Deactivate.DeactivateStaff.Handler>();
        services.AddScoped<Features.Staff.SetServices.SetStaffServices.Handler>();
        services.AddScoped<Features.Staff.SetAvailabilityRules.SetAvailabilityRules.Handler>();
        services.AddScoped<Features.Staff.AddAvailabilityOverride.AddAvailabilityOverride.Handler>();
        services.AddScoped<Features.Staff.RemoveAvailabilityOverride.RemoveAvailabilityOverride.Handler>();
        services.AddScoped<Features.Availability.GetSlots.GetSlots.Handler>();
        services.AddScoped<Features.Reservations.Create.CreateReservation.Handler>();
        services.AddScoped<Features.Reservations.Release.ReleaseReservation.Handler>();
        services.AddScoped<Features.Bookings.Create.CreateBooking.Handler>();
        services.AddScoped<Features.Bookings.Cancel.CancelBooking.Handler>();
        services.AddScoped<Features.Bookings.StartReschedule.StartReschedule.Handler>();
        services.AddScoped<Features.Bookings.ConfirmReschedule.ConfirmReschedule.Handler>();
        services.AddScoped<Features.Bookings.AdminList.AdminListBookings.Handler>();
        services.AddScoped<Features.Bookings.AdminGetById.AdminGetBookingById.Handler>();
        services.AddScoped<Features.Bookings.AdminCreate.AdminCreateBooking.Handler>();
        services.AddScoped<Features.Bookings.MarkNoShow.MarkBookingNoShow.Handler>();
        services.AddScoped<Features.Bookings.AddInternalNote.AddBookingInternalNote.Handler>();
        services.AddScoped<Features.Bookings.ExportCsv.ExportBookingsCsv.Handler>();
        services.AddScoped<Features.Bookings.DownloadIcal.DownloadBookingIcal.Handler>();
        services.AddScoped<Features.RecurringBookings.Create.CreateRecurringBooking.Handler>();
        services.AddScoped<Features.RecurringBookings.Cancel.CancelRecurringBooking.Handler>();
        services.AddScoped<Features.RecurringBookings.List.ListRecurringBookings.Handler>();
        services.AddScoped<Features.WebhookEndpoints.Create.CreateWebhookEndpoint.Handler>();
        services.AddScoped<Features.WebhookEndpoints.Update.UpdateWebhookEndpoint.Handler>();
        services.AddScoped<Features.WebhookEndpoints.Delete.DeleteWebhookEndpoint.Handler>();
        services.AddScoped<Features.WebhookEndpoints.List.ListWebhookEndpoints.Handler>();
        services.AddScoped<Features.WebhookEndpoints.GetDeliveries.GetWebhookDeliveries.Handler>();
        services.AddScoped<Features.WebhookEndpoints.RetryDelivery.RetryWebhookDelivery.Handler>();

        return services;
    }
}
