using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public sealed class AppointmentService(
    Dsw2026TpiDbContext context,
    AuthenticationDbContext authenticationContext,
    TimeProvider timeProvider,
    ILogger<AppointmentService> logger) : IAppointmentService
{
    private readonly Dsw2026TpiDbContext _context = context;
    private readonly AuthenticationDbContext _authenticationContext = authenticationContext;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<AppointmentService> _logger = logger;

    public async Task<AppointmentModel.Response> CreateAsync(
        AppointmentModel.Request request,
        string patientEmail)
    {
        ValidateRequest(request);
        await EnsurePatientOwnershipAsync(request.Patient.Dni, patientEmail);

        var slot = await _context.AvailabilitySlots
            .AsNoTracking()
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
        var slotStart = slot.SlotDate.ToDateTime(slot.StartTime);
        if (slotStart <= now.DateTime)
        {
            throw Invalid("availabilitySlotId", "El turno debe corresponder a una fecha y hora futuras.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // La actualización condicional la resuelve SQLite de forma atómica: sólo una solicitud puede
            // cambiar el slot de AVAILABLE a BOOKED, aun si ambas lo leyeron disponible al mismo tiempo.
            // No dependemos de un UNIQUE sobre APPOINTMENTS.AvailabilitySlotId: CancelAsync conserva la fila
            // histórica como CANCELLED y devuelve el slot a AVAILABLE, por lo que una reserva posterior crea
            // otra fila BOOKED para el mismo slot. AppointmentsApiTests demuestra ambos comportamientos.
            var reservedSlots = await _context.AvailabilitySlots
                .Where(candidate =>
                    candidate.Id == slot.Id &&
                    candidate.Status == "AVAILABLE" &&
                    !candidate.Deleted)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.Status, "BOOKED")
                    .SetProperty(candidate => candidate.UpdatedAt, now.UtcDateTime));

            if (reservedSlots == 0)
            {
                throw SlotConflict();
            }

            var appointment = new Appointment
            {
                DoctorId = request.DoctorId,
                AvailabilitySlotId = slot.Id,
                PatientDni = request.Patient.Dni,
                Reason = request.Reason.Trim(),
                Status = AppointmentStatus.BOOKED,
                CreatedAt = now.UtcDateTime,
                UpdatedAt = now.UtcDateTime
            };
            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Cita {AppointmentId} reservada para el slot {SlotId}",
                appointment.Id,
                slot.Id);
            return await GetResponseAsync(appointment.Id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyCollection<AppointmentModel.Response>> GetActiveForPatientAsync(
        string dni,
        string patientEmail)
    {
        ValidateDni(dni);
        await EnsurePatientOwnershipAsync(dni, patientEmail);

        var query = AppointmentsQuery()
                .Where(appointment =>
                    appointment.PatientDni == dni &&
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
                appointment.PatientDni == dni &&
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
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id)
            ?? throw new EntityNotFoundException("Cita");
        await EnsurePatientOwnershipAsync(appointment.PatientDni, patientEmail);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // La condición sobre BOOKED expresa la única transición permitida y evita dos cancelaciones simultáneas.
            // Cancelar no borra la cita: BOOKED pasa a CANCELLED para preservar la historia clínica/operativa.
            var cancelled = await _context.Appointments
                .Where(candidate =>
                    candidate.Id == id &&
                    candidate.Status == AppointmentStatus.BOOKED)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(candidate => candidate.Status, AppointmentStatus.CANCELLED)
                    .SetProperty(candidate => candidate.UpdatedAt, now));
            if (cancelled == 0)
            {
                throw new ConflictException(
                    "APPOINTMENT_INVALID_STATUS",
                    "Sólo se puede cancelar una cita en estado BOOKED.");
            }

            // En la misma transacción el slot vuelve a AVAILABLE. Luego CreateAsync puede crear una NUEVA fila
            // BOOKED que reutilice AvailabilitySlotId, mientras la fila CANCELLED anterior permanece consultable.
            await _context.AvailabilitySlots
                .Where(slot => slot.Id == appointment.AvailabilitySlotId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(slot => slot.Status, "AVAILABLE")
                    .SetProperty(slot => slot.UpdatedAt, now));
            await transaction.CommitAsync();
            _logger.LogInformation("Cita {AppointmentId} cancelada", id);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyCollection<AppointmentModel.Response>> GetByDateAsync(DateOnly date)
    {
        if (date == default) throw Invalid("date", "La fecha es obligatoria.");
        return await Project(AppointmentsQuery()
                .Where(appointment => appointment.AvailabilitySlot.SlotDate == date)
                .OrderBy(appointment => appointment.AvailabilitySlot.StartTime)
                .ThenBy(appointment => appointment.Doctor.Name))
            .ToListAsync();
    }

    public async Task<AppointmentModel.PagedResponse> SearchAsync(
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
            query = query.Where(appointment => appointment.Doctor.SpecialityId == specialtyId);
        if (doctorId.HasValue)
            query = query.Where(appointment => appointment.DoctorId == doctorId);
        if (dni is not null)
            query = query.Where(appointment => appointment.PatientDni == dni);
        if (date.HasValue)
            query = query.Where(appointment => appointment.AvailabilitySlot.SlotDate == date);

        var total = await query.CountAsync();
        var data = await Project(query
            .OrderBy(appointment => appointment.AvailabilitySlot.SlotDate)
            .ThenBy(appointment => appointment.AvailabilitySlot.StartTime)
            .ThenBy(appointment => appointment.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize))
            .ToListAsync();
        return new AppointmentModel.PagedResponse(pageIndex, pageSize, total, data);
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
            appointment.Doctor.SpecialityId,
            appointment.Doctor.Speciality == null ? null : appointment.Doctor.Speciality.Name,
            appointment.DoctorId,
            appointment.Doctor.Name,
            appointment.AvailabilitySlotId,
            appointment.AvailabilitySlot.SlotDate,
            appointment.AvailabilitySlot.StartTime,
            appointment.AvailabilitySlot.EndTime,
            appointment.PatientDni,
            appointment.Reason,
            appointment.Status.ToString()));

    private async Task<AppointmentModel.Response> GetResponseAsync(Guid id) =>
        await Project(AppointmentsQuery().Where(appointment => appointment.Id == id)).SingleAsync();

    private async Task EnsurePatientOwnershipAsync(string dni, string patientEmail)
    {
        var patient = await _authenticationContext.Set<ApplicationUser>()
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Dni == dni)
            ?? throw new EntityNotFoundException("Paciente");
        if (!string.Equals(patient.Email, patientEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationException();
        }
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

    private static ConflictException SlotConflict() =>
        (ConflictException)new ConflictException(
                "APPOINTMENT_CONFLICT",
                "El slot ya no está disponible.")
            .WithDetail("availabilitySlotId", "slot_unavailable");
}
