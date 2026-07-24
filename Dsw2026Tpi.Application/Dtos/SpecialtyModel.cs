using System.ComponentModel.DataAnnotations;

namespace Dsw2026Tpi.Application.Dtos;

public record SpecialtyModel
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int MaxPageIndex = 1_000_000;
    public const int MinNameLength = 3;
    public const int MaxNameLength = 100;
    public const int MinDescriptionLength = 10;
    public const int MaxDescriptionLength = 100;

    public record Query(
        [Range(0, MaxPageIndex, ErrorMessage = "El índice de página debe estar entre 0 y 1000000.")]
        int PageIndex = 0,

        [Range(MinPageSize, MaxPageSize, ErrorMessage = "El tamaño de página debe estar entre 1 y 100.")]
        int PageSize = 10,

        [StringLength(MaxNameLength, MinimumLength = MinNameLength,
            ErrorMessage = "El nombre debe tener entre 3 y 100 caracteres.")]
        string? Name = null
    );

    public record Request(
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(MaxNameLength, MinimumLength = MinNameLength,
            ErrorMessage = "El nombre debe tener entre 3 y 100 caracteres.")]
        string Name,

        [Required(ErrorMessage = "La descripción es obligatoria.")]
        [StringLength(MaxDescriptionLength, MinimumLength = MinDescriptionLength,
            ErrorMessage = "La descripción debe tener entre 10 y 100 caracteres.")]
        string Description
    );

    public record Response(
        Guid Id,
        string Name,
        string Description
    );

    public record PagedResponse(
        int PageSize,
        int PageIndex,
        List<Response> Data,
        int Total
    );
}
