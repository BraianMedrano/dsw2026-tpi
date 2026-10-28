using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public sealed class AppointmentService(
    Dsw2026TpiDbContext context,
    AuthenticationDbContext authenticationContext,
    IPatientRegistry patientRegistry,
    TimeProvider timeProvider,
    ILogger<AppointmentService> logger) : IAppointmentService
{
    private readonly Dsw2026TpiDbContext _context = context;
    private readonly AuthenticationDbContext _authenticationContext = authenticationContext;
    private readonly IPatientRegistry _patientRegistry = patientRegistry;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<AppointmentService> _logger = logger;

    public async Task<AppointmentModel.Response> CreateAsync(
        AppointmentModel.Request request,
        string patientEmail)
    {
        ValidateRequest(request);
        var user = await EnsurePatientOwnershipAsync(request.Patient.Dni, patientEmail);

        var slot = await _context.AvailabilitySlots
            .FirstOrDefaultAsync(candidate =>
                candidate.Id == request.AvailabilitySlotId &&
                !candidate.Deleted)
            ?? throw new EntityNotFoundException("Disponibilidad");

        if (slot.DoctorId != request.DoctorId ||
            !await _context.Set<Doctor>().AnyAsync(doctor => doctor.Id == request.DoctorId))
        {
            throw new EntityNotFoundException("Médico");
        }

        var now = _timeProvider.GetLocalNow();
        if (!slot.IsFutureAt(now.DateTime))
        {
            throw Invalid("availabilitySlotId", "El turno debe corresponder a una fecha y hora futuras.");
        }
        if (!slot.IsFree)
        {
            throw SlotConflict();
        }

        var patient = await _patientRegistry.EnsureAsync(request.Patient.Dni, user.Id);
        var appointment = patient.Book(slot, request.Reason, now.UtcDateTime);
        try
        {
            // Status es token de concurrencia: SaveChanges ejecuta UPDATE ... WHERE Status = 'AVAILABLE' y la
            // inserción de la cita en una sola transacción. Si otra solicitud reservó el turno después de leerlo,
            // el UPDATE no afecta filas y EF lanza DbUpdateConcurrencyException, sin crear la cita.
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || IsUniqueConstraintViolation(exception))
        {
            throw SlotConflict();
        }

        _logger.LogInformation(
            "Cita {AppointmentId} reservada para el slot {SlotId}",
            appointment.Id,
            slot.Id);
        return await GetResponseAsync(appointment.Id);
    }

    public async Task<IReadOnlyCollection<AppointmentModel.Response>> GetActiveForPatientAsync(
        string dni,
        string patientEmail)
    {
        ValidateDni(dni);
        await EnsurePatientOwnershipAsync(dni, patientEmail);

        var query = AppointmentsQuery()
                .Where(appointment =>
                    appointment.Patient.Dni == dni &&
                    appointment.Status == AppointmentStatus.BOOKED)
            .OrderBy(appointment => appointment.AvailabilitySlot.SlotDate)
            .ThenBy(appointment => appointment.AvailabilitySlot.StartTime);
        return await Project(query)
            .ToListAsync();
    }

    public async Task<AppointmentModel.PagedResponse> GetHistoryForPatientAsync(
        string dni,
        string patientEmail,
        int pageIndex,
        int pageSize)
    {
        ValidateDni(dni);
        ValidatePagination(pageIndex, pageSize);
        await EnsurePatientOwnershipAsync(dni, patientEmail);

        var query = AppointmentsQuery()
            .Where(appointment =>
                appointment.Patient.Dni == dni &&
                (appointment.Status == AppointmentStatus.CANCELLED ||
                 appointment.Status == AppointmentStatus.ATTENDED ||
                 appointment.Status == AppointmentStatus.NO_SHOW));

        var total = await query.CountAsync();
        var data = await Project(query
                .OrderByDescending(appointment => appointment.AvailabilitySlot.SlotDate)
                .ThenByDescending(appointment => appointment.AvailabilitySlot.StartTime)
                .ThenByDescending(appointment => appointment.Id)
                .Skip(pageIndex * pageSize)
                .Take(pageSize))
            .ToListAsync();

        return new AppointmentModel.PagedResponse(pageIndex, pageSize, total, data);
    }

    public async Task CancelAsync(Guid id, string patientEmail)
    {
        var appointment = await _context.Appointments
            .Include(candidate => candidate.AvailabilitySlot)
            .Include(candidate => candidate.Patient)
            .FirstOrDefaultAsync(candidate => candidate.Id == id)
            ?? throw new EntityNotFoundException("Cita");
        await EnsurePatientOwnershipAsync(appointment.Patient.Dni, patientEmail);

        if (!appointment.IsActive)
        {
            throw InvalidStatus();
        }

        // Cancelar no borra la cita: pasa a CANCELLED con su fecha de cancelación y el turno vuelve a AVAILABLE.
        // Ambos cambios se guardan juntos; el token de concurrencia sobre Status evita dos cancelaciones simultáneas.
        appointment.Cancel(_timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InvalidStatus();
        }
        _logger.LogInformation("Cita {AppointmentId} cancelada", id);
    }

    public async Task<IReadOnlyCollection<AppointmentModel.Response>> GetByDateAsync(DateOnly date)
    {
        if (date == default) throw Invalid("date", "La fecha es obligatoria.");
        return await Project(AppointmentsQuery()
                .Where(appointment => appointment.AvailabilitySlot.SlotDate == date)
                .OrderBy(appointment => appointment.AvailabilitySlot.StartTime)
                .ThenBy(appointment => appointment.AvailabilitySlot.AvailabilityRule.Doctor.Name))
            .ToListAsync();
    }

    public async Task<AppointmentModel.AdminPagedResponse> SearchAsync(
        Guid? specialtyId,
        Guid? doctorId,
        string? dni,
        DateOnly? date,
        int pageIndex,
        int pageSize)
    {
        if (pageIndex < 0) throw Invalid("pageIndex", "El índice de página no puede ser negativo.");
        if (pageSize is < 1 or > 100) throw Invalid("pageSize", "El tamaño de página debe estar entre 1 y 100.");
        if (dni is not null) ValidateDni(dni);

        var query = AppointmentsQuery();
        if (specialtyId.HasValue)
            query = query.Where(appointment => appointment.AvailabilitySlot.AvailabilityRule.Doctor.SpecialityId == specialtyId);
        if (doctorId.HasValue)
            query = query.Where(appointment => appointment.AvailabilitySlot.DoctorId == doctorId);
        if (dni is not null)
            query = query.Where(appointment => appointment.Patient.Dni == dni);
        if (date.HasValue)
            query = query.Where(appointment => appointment.AvailabilitySlot.SlotDate == date);

        var total = await query.CountAsync();
        var page = await query
            .OrderBy(appointment => appointment.AvailabilitySlot.SlotDate)
            .ThenBy(appointment => appointment.AvailabilitySlot.StartTime)
            .ThenBy(appointment => appointment.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(appointment => new
            {
                appointment.Id,
                appointment.Status,
                PatientDni = appointment.Patient.Dni,
                PatientFullName = appointment.Patient.FullName,
                appointment.AvailabilitySlot.DoctorId,
                DoctorName = appointment.AvailabilitySlot.AvailabilityRule.Doctor.Name,
                SpecialtyId = appointment.AvailabilitySlot.AvailabilityRule.Doctor.SpecialityId,
                SpecialtyName = appointment.AvailabilitySlot.AvailabilityRule.Doctor.Speciality.Name
            })
            .ToListAsync();

        // El login del paciente sólo recibe email y DNI, por lo que el nombre puede no estar cargado.
        // La v1.7 permite enviarlo vacío.
        var data = page.Select(appointment => new AppointmentModel.AdminResponse(
            appointment.Id,
            appointment.Status.ToString(),
            new AppointmentModel.AdminPatientResponse(
                long.Parse(appointment.PatientDni),
                appointment.PatientFullName ?? string.Empty),
            new AppointmentModel.AdminDoctorResponse(
                appointment.DoctorId,
                appointment.DoctorName,
                new AppointmentModel.AdminSpecialtyResponse(appointment.SpecialtyId, appointment.SpecialtyName))))
            .ToList();
        return new AppointmentModel.AdminPagedResponse(pageIndex, pageSize, total, data);
    }

    private static void ValidatePagination(int pageIndex, int pageSize)
    {
        if (pageIndex is < 0 or > AppointmentModel.MaxPageIndex)
            throw Invalid("pageIndex", "El índice de página debe estar entre 0 y 1000000.");
        if (pageSize is < AppointmentModel.MinPageSize or > AppointmentModel.MaxPageSize)
            throw Invalid("pageSize", "El tamaño de página debe estar entre 1 y 100.");
    }

    private IQueryable<Appointment> AppointmentsQuery() =>
        _context.Appointments.AsNoTracking().IgnoreQueryFilters();

    private static IQueryable<AppointmentModel.Response> Project(IQueryable<Appointment> query) =>
        query.Select(appointment => new AppointmentModel.Response(
            appointment.Id,
            appointment.AvailabilitySlot.AvailabilityRule.Doctor.SpecialityId,
            appointment.AvailabilitySlot.AvailabilityRule.Doctor.Speciality.Name,
            appointment.AvailabilitySlot.DoctorId,
            appointment.AvailabilitySlot.AvailabilityRule.Doctor.Name,
            appointment.AvailabilitySlotId,
            appointment.AvailabilitySlot.SlotDate,
            appointment.AvailabilitySlot.StartTime,
            appointment.AvailabilitySlot.EndTime,
            appointment.Patient.Dni,
            appointment.Reason,
            appointment.Status.ToString()));

    private async Task<AppointmentModel.Response> GetResponseAsync(Guid id) =>
        await Project(AppointmentsQuery().Where(appointment => appointment.Id == id)).SingleAsync();

    private async Task<ApplicationUser> EnsurePatientOwnershipAsync(string dni, string patientEmail)
    {
        var patient = await _authenticationContext.Set<ApplicationUser>()
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Dni == dni)
            ?? throw new EntityNotFoundException("Paciente");
        if (!string.Equals(patient.Email, patientEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationException();
        }
        return patient;
    }

    private static void ValidateRequest(AppointmentModel.Request? request)
    {
        if (request is null) throw Invalid("request", "La solicitud es obligatoria.");
        if (request.DoctorId == Guid.Empty) throw Invalid("doctorId", "El médico es obligatorio.");
        if (request.AvailabilitySlotId == Guid.Empty) throw Invalid("availabilitySlotId", "La disponibilidad es obligatoria.");
        if (request.Patient is null) throw Invalid("patient", "El paciente es obligatorio.");
        ValidateDni(request.Patient.Dni);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw Invalid("reason", "El motivo debe tener al menos 5 caracteres.");
        if (request.Reason.Length > 500)
            throw Invalid("reason", "El motivo no puede superar los 500 caracteres.");
    }

    private static void ValidateDni(string? dni)
    {
        if (dni is null || dni.Length is < 7 or > 8 || !dni.All(char.IsAsciiDigit))
            throw Invalid("dni", "El DNI debe contener 7 u 8 dígitos.");
    }

    private static ValidationException Invalid(string field, string message) =>
        (ValidationException)new ValidationException(message, "APPOINTMENT_VALIDATION")
            .WithDetail(field, message);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 or 1555 };

    private static ConflictException InvalidStatus() =>
        new("APPOINTMENT_INVALID_STATUS", "Sólo se puede cancelar una cita en estado BOOKED.");

    private static ConflictException SlotConflict() =>
        (ConflictException)new ConflictException(
                "APPOINTMENT_CONFLICT",
                "El slot ya no está disponible.")
            .WithDetail("availabilitySlotId", "slot_unavailable");
}
