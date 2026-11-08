using System.ComponentModel.DataAnnotations;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Api.Services;

public sealed class InitialAdministratorBootstrapper
{
    private const string ConfigurationSection = "InitialAdministrator";
    private const int ConvergenceAttempts = 20;
    private static readonly TimeSpan ConvergenceDelay = TimeSpan.FromMilliseconds(50);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly AuthenticationDbContext _authenticationContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InitialAdministratorBootstrapper> _logger;

    public InitialAdministratorBootstrapper(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        AuthenticationDbContext authenticationContext,
        IConfiguration configuration,
        ILogger<InitialAdministratorBootstrapper> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _authenticationContext = authenticationContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var email = _configuration[$"{ConfigurationSection}:Email"]?.Trim();
        var password = _configuration[$"{ConfigurationSection}:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Se requiere {ConfigurationSection}. Configurá " +
                $"{ConfigurationSection}:Email y {ConfigurationSection}:Password " +
                "mediante secretos de usuario o variables de entorno.");
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            throw new InvalidOperationException(
                $"{ConfigurationSection}:Email debe ser un email válido.");
        }

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            await EnsureExistingUserIsAdministratorAsync(existingUser);
            _logger.LogInformation("El administrador inicial ya existe; no se realizaron cambios");
            return;
        }

        var now = DateTime.UtcNow;
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = now,
            UpdatedAt = now
        };

        await ValidatePasswordAsync(user, password);
        await EnsureAdministratorRoleAsync();

        // Identity guarda la cuenta y su rol en dos operaciones. La transacción hace que
        // ambas se confirmen juntas y evita dejar un usuario administrador incompleto.
        await using var transaction =
            await _authenticationContext.Database.BeginTransactionAsync();
        IdentityResult createResult;
        try
        {
            createResult = await _userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _authenticationContext.ChangeTracker.Clear();

            if (await WaitForConfiguredAdministratorAsync(email))
            {
                return;
            }

            throw;
        }

        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync();
            _authenticationContext.ChangeTracker.Clear();

            if (IsDuplicate(createResult.Errors) &&
                await WaitForConfiguredAdministratorAsync(email))
            {
                return;
            }

            throw BootstrapFailure(
                "No se pudo crear la cuenta del administrador inicial",
                createResult.Errors);
        }

        try
        {
            var roleResult = await _userManager.AddToRoleAsync(user, Roles.Administrator);
            if (!roleResult.Succeeded)
            {
                if (!await _userManager.IsInRoleAsync(user, Roles.Administrator))
                {
                    throw BootstrapFailure(
                        "No se pudo asignar el rol de administrador",
                        roleResult.Errors);
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        _logger.LogInformation("El administrador inicial fue creado correctamente");
    }

    private async Task EnsureExistingUserIsAdministratorAsync(ApplicationUser user)
    {
        if (await _userManager.IsInRoleAsync(user, Roles.Administrator))
        {
            return;
        }

        // Nunca eleva silenciosamente una cuenta creada mediante otro flujo de identidad.
        throw new InvalidOperationException(
            $"Una cuenta de Identity ya usa {ConfigurationSection}:Email " +
            "sin el rol de administrador. Resolvé la cuenta manualmente; " +
            "el bootstrap no la elevará.");
    }

    private async Task ValidatePasswordAsync(ApplicationUser user, string password)
    {
        var errors = new List<IdentityError>();
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors);
            }
        }

        if (errors.Count > 0)
        {
            throw BootstrapFailure(
                $"{ConfigurationSection}:Password no cumple los requisitos de Identity",
                errors);
        }
    }

    private async Task EnsureAdministratorRoleAsync()
    {
        if (await _roleManager.RoleExistsAsync(Roles.Administrator))
        {
            return;
        }

        try
        {
            var result = await _roleManager.CreateAsync(new IdentityRole(Roles.Administrator));
            if (result.Succeeded || await _roleManager.RoleExistsAsync(Roles.Administrator))
            {
                return;
            }

            throw BootstrapFailure("No se pudo crear el rol de administrador", result.Errors);
        }
        catch (DbUpdateException)
        {
            if (await _roleManager.RoleExistsAsync(Roles.Administrator))
            {
                // Otra instancia creó el rol entre la consulta y la inserción.
                return;
            }

            throw;
        }
    }

    private async Task<bool> WaitForConfiguredAdministratorAsync(string email)
    {
        for (var attempt = 0; attempt < ConvergenceAttempts; attempt++)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user is not null &&
                await _userManager.IsInRoleAsync(user, Roles.Administrator))
            {
                _logger.LogInformation(
                    "El administrador inicial fue creado concurrentemente por otra instancia");
                return true;
            }

            await Task.Delay(ConvergenceDelay);
        }

        var conflictingUser = await _userManager.FindByEmailAsync(email);
        if (conflictingUser is not null)
        {
            await EnsureExistingUserIsAdministratorAsync(conflictingUser);
        }

        return false;
    }

    private static bool IsDuplicate(IEnumerable<IdentityError> errors)
    {
        return errors.Any(error =>
            error.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                or nameof(IdentityErrorDescriber.DuplicateUserName));
    }

    private static InvalidOperationException BootstrapFailure(
        string message,
        IEnumerable<IdentityError> errors)
    {
        var errorCodes = string.Join(
            ", ",
            errors.Select(error => error.Code).Distinct(StringComparer.Ordinal));
        return new InvalidOperationException($"{message}: {errorCodes}.");
    }
}
