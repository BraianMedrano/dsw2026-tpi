using System.Globalization;
using System.Text;
using System.Text.Json;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services;

public class AvailabilityService(Dsw2026TpiDbContext context, TimeProvider timeProvider) : IAvailabilityService
{
    private const int SlotMinutes = 30;
    private readonly Dsw2026TpiDbContext _context = context;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<List<DoctorAvailabilityResponseDto>> GetDoctorAvailabilityAsync(Guid doctorId)
    {
        await EnsureActiveDoctorAsync(doctorId);
        var now = _timeProvider.GetLocalNow().DateTime;
        var rules = await CurrentRulesQuery(doctorId, now).AsNoTracking().ToListAsync();
        // Un médico nuevo es válido aunque todavía no tenga reglas; por eso se devuelve una colección vacía.
        return rules.Select(ToResponse).ToList();
    }

    public async Task<List<DoctorAvailabilityResponseDto>> CreateAvailabilityAsync(AvailabilityRequestDto request)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var candidates = ValidateAndNormalize(request);
        await EnsureActiveDoctorAsync(request.DoctorId);
        // El calendario es configuración obligatoria y se valida antes de abrir la transacción.
        var holidays = LoadNonWorkingDays();

        var currentRules = await CurrentRulesQuery(request.DoctorId, now).AsNoTracking().ToListAsync();
        EnsureOneRangePerDay(candidates, currentRules);

        // Una única unidad de trabajo guarda reglas y slots juntos: no quedan configuraciones a medio generar.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var created = AddRules(request.DoctorId, candidates, now, holidays);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return created.Select(ToResponse).ToList();
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync();
            throw DuplicateDayConflict();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<DoctorAvailabilityResponseDto>> UpdateAvailabilityAsync(AvailabilityRequestDto request)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var candidates = ValidateAndNormalize(request);
        await EnsureActiveDoctorAsync(request.DoctorId);
        var holidays = LoadNonWorkingDays();

        // PUT reemplaza el conjunto completo del mes dentro de una transacción; un fallo conserva las reglas anteriores.
        EnsureOneRangePerDay(candidates, []);
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var previous = await CurrentRulesQuery(request.DoctorId, now)
                .Include(rule => rule.Slots)
                .ToListAsync();
            _context.AvailabilityRules.RemoveRange(previous);

            var replacement = AddRules(request.DoctorId, candidates, now, holidays);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return replacement.Select(ToResponse).ToList();
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            await transaction.RollbackAsync();
            throw DuplicateDayConflict();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private IQueryable<AvailabilityRule> CurrentRulesQuery(Guid doctorId, DateTime now) =>
        _context.AvailabilityRules.Where(rule =>
            rule.DoctorId == doctorId && rule.Year == now.Year && rule.Month == now.Month && !rule.Deleted);

    private async Task EnsureActiveDoctorAsync(Guid doctorId)
    {
        // El filtro global de Doctor ya excluye inactivos; no se lo ignora porque el módulo sólo opera sobre médicos activos.
        if (doctorId == Guid.Empty || !await _context.Set<Doctor>().AnyAsync(doctor => doctor.Id == doctorId))
            throw new EntityNotFoundException("Médico");
    }

    private static List<NormalizedDay> ValidateAndNormalize(AvailabilityRequestDto? request)
    {
        if (request?.Days is null || request.Days.Count == 0)
            throw Invalid("days", "Debe indicar al menos un día de atención.");

        var normalized = new List<NormalizedDay>();
        foreach (var item in request.Days)
        {
            if (item is null || !TryNormalizeDay(item.Day, out var day))
                throw Invalid("days.day", "El día debe estar entre lunes y domingo.");
            if (!TryParseStrictTime(item.StartTime, out var start) || !TryParseStrictTime(item.EndTime, out var end))
                throw Invalid("days.time", "Las horas deben usar el formato HH:mm.");
            if (start >= end)
                throw Invalid("days", "La hora de inicio debe ser anterior a la hora de fin.");
            if (start.Minute % SlotMinutes != 0 || end.Minute % SlotMinutes != 0)
                throw Invalid("days", "Los rangos deben comenzar y terminar en intervalos completos de 30 minutos.");
            normalized.Add(new NormalizedDay(day, start, end));
        }
        return normalized;
    }

    private static void EnsureOneRangePerDay(IEnumerable<NormalizedDay> candidates, IEnumerable<AvailabilityRule> existing)
    {
        // El modelo del TPI tiene un elemento por día, con un único rango horario; no representa turnos partidos.
        var days = candidates.Select(day => day.Day).Concat(existing.Select(rule => rule.DayOfWeek));
        if (days.GroupBy(day => day).Any(group => group.Count() > 1))
            throw DuplicateDayConflict();
    }


    private List<AvailabilityRule> AddRules(
        Guid doctorId,
        IEnumerable<NormalizedDay> days,
        DateTime now,
        HashSet<DateOnly> holidays)
    {
        var result = new List<AvailabilityRule>();
        foreach (var day in days)
        {
            var rule = new AvailabilityRule { DoctorId = doctorId, Year = now.Year, Month = now.Month, DayOfWeek = day.Day, StartTime = day.Start, EndTime = day.End };
            GenerateFutureSlots(rule, now, holidays);
            _context.AvailabilityRules.Add(rule);
            result.Add(rule);
        }
        return result;
    }

    private static void GenerateFutureSlots(AvailabilityRule rule, DateTime now, HashSet<DateOnly> holidays)
    {
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);
        var targetDay = Enum.Parse<DayOfWeek>(rule.DayOfWeek);
        var lastDay = DateTime.DaysInMonth(rule.Year, rule.Month);
        // Se recorre únicamente el resto del mes y se descartan pasado y feriados antes de crear entidades.
        for (var number = today.Day; number <= lastDay; number++)
        {
            var date = new DateOnly(rule.Year, rule.Month, number);
            if (date.DayOfWeek != targetDay || holidays.Contains(date)) continue;
            for (var start = rule.StartTime; start.AddMinutes(SlotMinutes) <= rule.EndTime; start = start.AddMinutes(SlotMinutes))
            {
                if (date == today && start <= currentTime) continue;
                rule.Slots.Add(new AvailabilitySlot { DoctorId = rule.DoctorId, SlotDate = date, StartTime = start, EndTime = start.AddMinutes(SlotMinutes), Status = "AVAILABLE" });
            }
        }
    }

    private static HashSet<DateOnly> LoadNonWorkingDays()
    {
        // El override se resuelve en cada solicitud para aislar pruebas y configuraciones de cada proceso.
        var path = Environment.GetEnvironmentVariable("AVAILABILITY_NON_WORKING_DAYS_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "Sources", "non-working-days.json");
        if (!File.Exists(path))
            throw CalendarUnavailable();

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<NonWorkingDaysFile>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (file?.Dates is null)
                throw CalendarUnavailable();

            var dates = new HashSet<DateOnly>();
            foreach (var value in file.Dates)
            {
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    throw CalendarUnavailable();
                dates.Add(date);
            }
            return dates;
        }
        catch (ServiceUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw CalendarUnavailable(exception);
        }
    }

    private static bool TryNormalizeDay(string? input, out string day)
    {
        var key = (input ?? string.Empty).Trim().Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Aggregate(string.Empty, (text, character) => text + character).ToUpperInvariant();
        day = key switch { "LUNES" => nameof(DayOfWeek.Monday), "MARTES" => nameof(DayOfWeek.Tuesday), "MIERCOLES" => nameof(DayOfWeek.Wednesday), "JUEVES" => nameof(DayOfWeek.Thursday), "VIERNES" => nameof(DayOfWeek.Friday), "SABADO" => nameof(DayOfWeek.Saturday), "DOMINGO" => nameof(DayOfWeek.Sunday), _ => string.Empty };
        return day.Length > 0;
    }

    private static bool TryParseStrictTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    private static ValidationException Invalid(string field, string message) =>
        (ValidationException)new ValidationException(message, "AVAILABILITY_VALIDATION").WithDetail(field, message);
    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 };
    // La validación devuelve un error entendible; el índice único confirma la misma regla ante solicitudes concurrentes.
    private static ConflictException DuplicateDayConflict() =>
        new("AVAILABILITY_DUPLICATE_DAY", "Cada día admite un único rango horario.");
    private static ServiceUnavailableException CalendarUnavailable(Exception? innerException = null) =>
        new("AVAILABILITY_CALENDAR_UNAVAILABLE", "El calendario de días no laborables no está disponible o es inválido.", innerException);

    private static DoctorAvailabilityResponseDto ToResponse(AvailabilityRule rule) => new() { Id = rule.Id, Day = ToSpanishDay(Enum.Parse<DayOfWeek>(rule.DayOfWeek)), StartTime = rule.StartTime.ToString("HH:mm"), EndTime = rule.EndTime.ToString("HH:mm") };
    private static string ToSpanishDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "LUNES", DayOfWeek.Tuesday => "MARTES", DayOfWeek.Wednesday => "MIÉRCOLES",
        DayOfWeek.Thursday => "JUEVES", DayOfWeek.Friday => "VIERNES", DayOfWeek.Saturday => "SÁBADO", _ => "DOMINGO"
    };

    private sealed record NormalizedDay(string Day, TimeOnly Start, TimeOnly End);
    private sealed class NonWorkingDaysFile { public List<string>? Dates { get; init; } }
}
