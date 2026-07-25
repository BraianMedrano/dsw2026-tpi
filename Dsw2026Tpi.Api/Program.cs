using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.Api.Middlewares;
using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Dsw2026Tpi.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Inicializar con un logger simple antes de construir el host
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Iniciando aplicación Dsw2026Tpi.Api");

            var builder = WebApplication.CreateBuilder(args);

            //Configuraciones personalizadas
            builder.AddSerilogConfiguration();
            builder.Services.AddAppIdentity();
            builder.Services.AddAppAuthentication(builder.Configuration);
            builder.Services.AddSwaggerConfiguration();
            builder.Services.AddApplicationPersistence(builder.Configuration);
            builder.Services.AddAppCors(builder.Configuration);
            builder.Services.AddAppDependencies();
            builder.Services.AddControllers()
                .ConfigureApiBehaviorOptions(options =>
                {
                    options.InvalidModelStateResponseFactory = context =>
                    {
                        var error = new ErrorResponse(
                            nameof(ErrorCodes.VALIDATION_ERROR),
                            ErrorCodes.VALIDATION_ERROR);
                        error.AddDetail(
                            context.ModelState
                                .Where(entry => entry.Value?.Errors.Count > 0)
                                .SelectMany(entry => entry.Value!.Errors.Select(modelError =>
                                    (entry.Key, modelError.ErrorMessage))));
                        return new BadRequestObjectResult(error);
                    };
                });
            builder.Services.AddHealthChecks();

            var app = builder.Build();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var initializer =
                    scope.ServiceProvider.GetRequiredService<ApplicationStartupInitializer>();
                await initializer.InitializeAsync();
            }

            app.UseSerilogRequestLogging();

            if (app.Environment.IsProduction())
            {
                app.UseHttpsRedirection();
            }
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseCors();

            app.MapControllers();
            // La consigna deja públicos únicamente los dos login; el estado interno también requiere token.
            app.MapHealthChecks("/health-check").RequireAuthorization();

            Log.Information("Aplicación iniciada correctamente");

            await app.RunAsync();
        }
        catch (HostAbortedException)
        {
            Log.Information("El host fue abortado (normal durante migraciones de EF Core)");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "La aplicación falló al iniciar");
            throw;
        }
        finally
        {
            Log.Information("Cerrando aplicación");
            await Log.CloseAndFlushAsync();
        }
    }
}

