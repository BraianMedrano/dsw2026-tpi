using System.ComponentModel.DataAnnotations;

namespace Dsw2026Tpi.Application.Dtos;

public record LoginAdminModel
{
    // [ApiController] transforma estas restricciones en una respuesta 400 antes de ejecutar el endpoint.
    // Las propiedades son explícitas porque ASP.NET Core 10 no admite metadatos de validación
    // en las propiedades generadas por el constructor primario de un record.
    public sealed record Request
    {
        [Required, EmailAddress]
        public string Email { get; init; }

        [Required, MinLength(8)]
        public string Password { get; init; }

        public Request(string email, string password)
        {
            Email = email;
            Password = password;
        }
    }

    public record Response(string? Token, string? Role);
}

public record LoginPatientModel
{
    public sealed record Request
    {
        [Required, EmailAddress]
        public string Email { get; init; }

        // El DNI es texto porque no se usa para calcular y debe conservar exactamente los dígitos recibidos.
        [Required, RegularExpression(@"^[0-9]{7,8}$")]
        public string Dni { get; init; }

        public Request(string email, string dni)
        {
            Email = email;
            Dni = dni;
        }
    }

    public record Response(string? Token, string? Role);
}
