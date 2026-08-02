using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Dsw2026Tpi.Api.Configurations;

public sealed class ApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int AdminLoginPermitLimit { get; init; }

    [Range(1, int.MaxValue)]
    public int PatientLoginPermitLimit { get; init; }

    [Range(1, int.MaxValue)]
    public int AppointmentCreationPermitLimit { get; init; }

    [Range(1, int.MaxValue)]
    public int AuthenticatedPermitLimit { get; init; }

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; init; }

    [Range(0, 0)]
    public int QueueLimit { get; init; }
}

public static class RateLimitingConfigurationExtensions
{
    private const string AdminLoginPath = "/api/auth/admin/login";
    private const string PatientLoginPath = "/api/auth/patient/login";
    private const string AppointmentsPath = "/api/appointments";

    public static IServiceCollection AddAppRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(ApiRateLimitingOptions.SectionName);
        services.AddOptions<ApiRateLimitingOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<ApiRateLimitingOptions>>((options, configuredSettings) =>
        {
            var settings = configuredSettings.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var selection = SelectPartition(context, settings);
                return RateLimitPartition.GetFixedWindowLimiter(
                    selection.Key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = selection.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = settings.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    });
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning(
                    "Rate limit excedido para {Method} {Endpoint}",
                    context.HttpContext.Request.Method,
                    context.HttpContext.GetEndpoint()?.DisplayName ?? "endpoint desconocido");

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ErrorResponse(
                        nameof(ErrorCodes.RATE_LIMIT_EXCEEDED),
                        ErrorCodes.RATE_LIMIT_EXCEEDED),
                    cancellationToken);
            };
        });

        return services;
    }

    private static (string Key, int PermitLimit) SelectPartition(
        HttpContext context,
        ApiRateLimitingOptions settings)
    {
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');

        // Cada request consume un solo bucket: las rutas sensibles reemplazan al límite general.
        if (HttpMethods.IsPost(context.Request.Method) &&
            path.Equals(AdminLoginPath, StringComparison.OrdinalIgnoreCase))
        {
            return ($"admin-login:ip:{ClientIp(context)}", settings.AdminLoginPermitLimit);
        }

        if (HttpMethods.IsPost(context.Request.Method) &&
            path.Equals(PatientLoginPath, StringComparison.OrdinalIgnoreCase))
        {
            return ($"patient-login:ip:{ClientIp(context)}", settings.PatientLoginPermitLimit);
        }

        if (HttpMethods.IsPost(context.Request.Method) &&
            path.Equals(AppointmentsPath, StringComparison.OrdinalIgnoreCase))
        {
            return ($"appointment:{UserOrIp(context)}", settings.AppointmentCreationPermitLimit);
        }

        return ($"authenticated:{UserOrIp(context)}", settings.AuthenticatedPermitLimit);
    }

    private static string UserOrIp(HttpContext context)
    {
        var name = context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name
            : null;
        return !string.IsNullOrWhiteSpace(name)
            ? $"user:{name.Trim().ToUpperInvariant()}"
            : $"ip:{ClientIp(context)}";
    }

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown-remote-address";
}
