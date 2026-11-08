using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services;

public sealed class PatientRegistry(Dsw2026TpiDbContext context, TimeProvider timeProvider) : IPatientRegistry
{
    private readonly Dsw2026TpiDbContext _context = context;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<Patient> EnsureAsync(string dni, string? userId)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var patient = await _context.Patients.SingleOrDefaultAsync(candidate => candidate.Dni == dni);
        if (patient is not null)
        {
            if (userId is not null && patient.UserId != userId)
            {
                patient.LinkUser(userId, now);
                await _context.SaveChangesAsync();
            }
            return patient;
        }

        patient = new Patient(dni, userId, now);
        _context.Patients.Add(patient);
        try
        {
            await _context.SaveChangesAsync();
            return patient;
        }
        catch (DbUpdateException)
        {
            // Otra solicitud creó el mismo paciente entre la consulta y la inserción; el índice único del DNI lo impide.
            _context.Entry(patient).State = EntityState.Detached;
            return await _context.Patients.SingleOrDefaultAsync(candidate => candidate.Dni == dni)
                ?? throw new InvalidOperationException("No se pudo registrar el paciente.");
        }
    }
}
