using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Helpers;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    // Evita que dos primeros ingresos simultáneos creen el mismo paciente dentro de esta instancia.
    // El índice único del DNI mantiene la protección cuando existen varias instancias de la API.
    private static readonly SemaphoreSlim PatientCreationLock = new(1, 1);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISignInService _signInManager;
    private readonly AuthenticationDbContext _authenticationContext;
    private readonly JwtService _jwtService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(UserManager<ApplicationUser> userManager,
        ISignInService signInManager,
        AuthenticationDbContext authenticationContext,
        JwtService jwtService,
        ILogger<AuthenticationService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _authenticationContext = authenticationContext;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginAdminModel.Response> LoginAdmin(LoginAdminModel.Request request)
    {
        // Aca parece redundante la validacion del email pero lo protege cuando no se llama desde el controlador, por ejemplo desde un test unitario, ya que el controller no se ejecuta y no valida el DTO. Por eso se valida aca tambien.
        if (!request.Email.IsEmailValid()) throw new ValidationException();
        // Si el usuario no existe, respondemos 401. No informamos “ese email no está registrado”, porque eso permitiría enumerar cuentas existentes.
        var user = await _userManager.FindByEmailAsync(request.Email) ?? throw new AuthenticationException();
        var passwordIsValid = await _signInManager.CheckPassword(user, request.Password);
        var isAdministrator = passwordIsValid &&
            await _userManager.IsInRoleAsync(user, Roles.Administrator);

        if (!isAdministrator)
        {
            _logger.LogError("Intento de login fallido para: {Email}", request.Email);
            throw new AuthenticationException();
        }
        // Estos casos producen el mismo 401:
        // -Usuario inexistente.
        // - Contraseña incorrecta.
        // - Usuario válido, pero no administrador.
        
        //Así no revelamos cuál condición falló. 

        var token = _jwtService.GenerateToken(user.UserName!, Roles.Administrator);

        return new LoginAdminModel.Response(
            token,
            Roles.Administrator.ToUpperInvariant()
        );
    }

    public async Task<LoginPatientModel.Response> LoginPatient(LoginPatientModel.Request request)
    {
        // También validamos en el servicio porque puede invocarse sin pasar por el controlador,
        // por ejemplo desde pruebas o desde otro caso de uso.
        if (!request.Email.IsEmailValid() ||
            string.IsNullOrEmpty(request.Dni) ||
            request.Dni.Length is < 7 or > 8 ||
            !request.Dni.All(char.IsAsciiDigit))
        {
            throw new ValidationException();
        }

        await PatientCreationLock.WaitAsync();
        try
        {
            var patient = await FindExistingPatientAsync(request.Email, request.Dni);
            patient ??= await CreatePatientAsync(request.Email, request.Dni);

            var token = _jwtService.GenerateToken(patient.UserName!, Roles.Patient);
            return new LoginPatientModel.Response(
                token,
                Roles.Patient.ToUpperInvariant());
        }
        finally
        {
            PatientCreationLock.Release();
        }
    }

    private async Task<ApplicationUser?> FindExistingPatientAsync(string email, string dni)
    {
        var userByEmail = await _userManager.FindByEmailAsync(email);
        var userByDni = await _userManager.Users.SingleOrDefaultAsync(user => user.Dni == dni);

        if (userByEmail is null && userByDni is null) return null;

        var samePatient = userByEmail is not null &&
            userByDni is not null &&
            userByEmail.Id == userByDni.Id &&
            await _userManager.IsInRoleAsync(userByEmail, Roles.Patient);

        if (!samePatient)
        {
            // Email inexistente, DNI inexistente, datos cruzados o rol incorrecto devuelven el mismo 401.
            // De ese modo la respuesta no permite averiguar qué dato ya está registrado.
            _logger.LogWarning("Intento de login de paciente con identidad inconsistente");
            throw new AuthenticationException();
        }

        return userByEmail;
    }

    private async Task<ApplicationUser> CreatePatientAsync(string email, string dni)
    {
        await using var transaction = await _authenticationContext.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var patient = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Dni = dni,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            var creationResult = await _userManager.CreateAsync(patient);
            if (!creationResult.Succeeded)
            {
                await transaction.RollbackAsync();
                _authenticationContext.ChangeTracker.Clear();

                // Otra instancia puede haber ganado la carrera entre la consulta y la inserción.
                var concurrentPatient = await FindExistingPatientAsync(email, dni);
                if (concurrentPatient is not null) return concurrentPatient;

                throw CreateIdentityOperationException("crear", creationResult);
            }

            var roleResult = await _userManager.AddToRoleAsync(patient, Roles.Patient);
            if (!roleResult.Succeeded)
            {
                // La transacción evita dejar una cuenta sin rol si falla el segundo paso.
                await transaction.RollbackAsync();
                throw CreateIdentityOperationException("asignar el rol a", roleResult);
            }

            await transaction.CommitAsync();
            return patient;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            _authenticationContext.ChangeTracker.Clear();

            var concurrentPatient = await FindExistingPatientAsync(email, dni);
            if (concurrentPatient is not null) return concurrentPatient;
            throw;
        }
    }

    private static InvalidOperationException CreateIdentityOperationException(
        string operation,
        IdentityResult result)
    {
        var codes = string.Join(", ", result.Errors.Select(error => error.Code));
        return new InvalidOperationException(
            $"Identity no pudo {operation} el paciente. Códigos: {codes}");
    }

}
