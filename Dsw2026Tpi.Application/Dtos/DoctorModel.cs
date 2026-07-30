using System.ComponentModel.DataAnnotations;

namespace Dsw2026Tpi.Application.Dtos;

public record DoctorModel
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int MaxPageIndex = 1_000_000;
    public const int MinNameLength = 3;
    public const int MaxNameLength = 100;

    public record Query : IValidatableObject
    {
        public int PageIndex { get; init; }
        public int PageSize { get; init; } = 10;
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string? Name
        {
            get;
            init => field = value?.Trim();
        }

        public Query() { }
        public Query(int pageIndex = 0, int pageSize = 10, string? name = null)
        {
            PageIndex = pageIndex;
            PageSize = pageSize;
            Name = name?.Trim();
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (PageIndex is < 0 or > MaxPageIndex)
            {
                yield return new ValidationResult(
                    "El índice de página debe estar entre 0 y 1000000.",
                    [nameof(PageIndex)]);
            }

            if (PageSize is < MinPageSize or > MaxPageSize)
            {
                yield return new ValidationResult(
                    "El tamaño de página debe estar entre 1 y 100.",
                    [nameof(PageSize)]);
            }

            if (Name is not null && (Name.Length is < MinNameLength or > MaxNameLength))
            {
                yield return new ValidationResult(
                    "El nombre debe tener entre 3 y 100 caracteres.",
                    [nameof(Name)]);
            }
        }
    }

    public record Request : IValidatableObject
    {
        public string? Name
        {
            get;
            init => field = value?.Trim();
        }
        public string? LicenseNumber
        {
            get;
            init => field = value?.Trim();
        }
        public Guid? SpecialityId { get; init; }

        public Request() { }
        public Request(string? name, string? licenseNumber, Guid? specialityId)
        {
            Name = name?.Trim();
            LicenseNumber = licenseNumber?.Trim();
            SpecialityId = specialityId;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(
                    "El nombre es obligatorio.",
                    [nameof(Name)]);
            }
            else if (Name.Length is < MinNameLength or > MaxNameLength)
            {
                yield return new ValidationResult(
                    "El nombre debe tener entre 3 y 100 caracteres.",
                    [nameof(Name)]);
            }

            if (string.IsNullOrWhiteSpace(LicenseNumber))
            {
                yield return new ValidationResult(
                    "La matrícula es obligatoria.",
                    [nameof(LicenseNumber)]);
            }

            if (SpecialityId is null || SpecialityId == Guid.Empty)
            {
                yield return new ValidationResult(
                    "La especialidad es obligatoria.",
                    [nameof(SpecialityId)]);
            }
        }
    }

    // Los registros anteriores pueden no tener especialidad, pero las altas y modificaciones siguen exigiéndola.
    public record Response(Guid Id, string Name, string LicenseNumber, SpecialityDto? Specialty);
    public record SpecialityDto(Guid Id, string Name);
    public record PagedResponse(int PageSize, int PageIndex, List<Response> Data, int Total);
}
