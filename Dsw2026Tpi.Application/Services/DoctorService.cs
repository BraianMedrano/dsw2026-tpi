using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly Dsw2026TpiDbContext _context;

    public DoctorService(Dsw2026TpiDbContext context)
    {
        _context = context;
    }

    public async Task<DoctorModel.PagedResponse> GetAllAsync(
        int pageSize,
        int pageIndex,
        string? name = null)
    {
        var query = _context.Set<Doctor>()
            .AsNoTracking()
            .Where(doctor => !doctor.Speciality.Deleted)
            .AsQueryable();

        if (name is not null)
        {
            query = query.Where(doctor => doctor.Name.Contains(name));
        }

        var total = await query.CountAsync();

        var data = await query
            .OrderBy(doctor => doctor.Name)
            .ThenBy(doctor => doctor.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(doctor => new DoctorModel.Response(
                doctor.Id,
                doctor.Name,
                doctor.LicenseNumber,
                new DoctorModel.SpecialityDto(doctor.SpecialityId, doctor.Speciality.Name)))
            .ToListAsync();

        return new DoctorModel.PagedResponse(pageSize, pageIndex, data, total);
    }

    // F03: detalle de un médico para el formulario de edición del frontend
    // (/admin/doctors/:id/edit). Usa el mismo filtro y la misma forma que el listado,
    // así la pantalla recibe exactamente los datos que ya conoce.
    public async Task<DoctorModel.Response> GetByIdAsync(Guid id) =>
        await _context.Set<Doctor>()
            .AsNoTracking()
            .Where(doctor => doctor.Id == id && !doctor.Speciality.Deleted)
            .Select(doctor => new DoctorModel.Response(
                doctor.Id,
                doctor.Name,
                doctor.LicenseNumber,
                new DoctorModel.SpecialityDto(doctor.SpecialityId, doctor.Speciality.Name)))
            .FirstOrDefaultAsync()
        ?? throw new EntityNotFoundException("Médico");

    public async Task<DoctorModel.Response> CreateAsync(DoctorModel.Request request)
    {
        var specialityId = ValidateSpecialtyId(request.SpecialtyId);
        var speciality = await FindActiveSpecialityAsync(specialityId);
        var doctor = new Doctor(request.Name!, request.LicenseNumber!, speciality);

        _context.Set<Doctor>().Add(doctor);
        await _context.SaveChangesAsync();

        return Map(doctor);
    }

    public async Task<DoctorModel.Response> UpdateAsync(Guid id, DoctorModel.Request request)
    {
        var doctor = await _context.Set<Doctor>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id)
            ?? throw new EntityNotFoundException("Médico");
        var specialityId = ValidateSpecialtyId(request.SpecialtyId);
        var speciality = await FindActiveSpecialityAsync(specialityId);

        doctor.Update(request.Name!, request.LicenseNumber!, speciality);
        await _context.SaveChangesAsync();

        return Map(doctor);
    }

    public async Task DeleteAsync(Guid id)
    {
        var doctor = await _context.Set<Doctor>()
            .FirstOrDefaultAsync(candidate => candidate.Id == id)
            ?? throw new EntityNotFoundException("Médico");

        doctor.MarkAsDeleted();
        await _context.SaveChangesAsync();
    }

    private async Task<Speciality> FindActiveSpecialityAsync(Guid specialityId) =>
        await _context.Set<Speciality>()
            .FirstOrDefaultAsync(speciality => speciality.Id == specialityId)
        ?? throw new EntityNotFoundException("Especialidad");

    private static Guid ValidateSpecialtyId(Guid? specialtyId)
    {
        if (specialtyId is null || specialtyId == Guid.Empty)
        {
            throw new ValidationException()
                .WithDetail(
                    nameof(DoctorModel.Request.SpecialtyId),
                    "La especialidad es obligatoria.");
        }

        return specialtyId.Value;
    }

    private static DoctorModel.Response Map(Doctor doctor) =>
        new(
            doctor.Id,
            doctor.Name,
            doctor.LicenseNumber,
            new DoctorModel.SpecialityDto(doctor.SpecialityId, doctor.Speciality.Name));
}
