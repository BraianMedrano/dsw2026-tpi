using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services;

public class SpecialtyService : ISpecialtyService
{
    private readonly Dsw2026TpiDbContext _context;

    public SpecialtyService(Dsw2026TpiDbContext context)
    {
        _context = context;
    }

    public async Task<SpecialtyModel.PagedResponse> GetAllAsync(int pageSize, int pageIndex, string? name)
    {
        var query = _context.Set<Speciality>()
            .Where(s => !EF.Property<bool>(s, "Deleted"))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(s => s.Name.Contains(name));
        }

        var total = await query.CountAsync();

        var data = await query
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(s => new SpecialtyModel.Response(s.Id, s.Name, s.Description))
            .ToListAsync();

        return new SpecialtyModel.PagedResponse(pageSize, pageIndex, data, total);
    }

    public async Task<SpecialtyModel.Response> CreateAsync(SpecialtyModel.Request request)
    {
        var specialty = new Speciality(request.Name, request.Description);

        _context.Set<Speciality>().Add(specialty);
        await _context.SaveChangesAsync();

        return new SpecialtyModel.Response(specialty.Id, specialty.Name, specialty.Description);
    }

    public async Task<bool> UpdateAsync(Guid id, SpecialtyModel.Request request)
    {
        var specialty = await _context.Set<Speciality>()
            .FirstOrDefaultAsync(s => s.Id == id && !EF.Property<bool>(s, "Deleted"));

        if (specialty == null) return false;

        _context.Entry(specialty).CurrentValues.SetValues(new
        {
            Name = request.Name,
            Description = request.Description
        });

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var specialty = await _context.Set<Speciality>()
            .FirstOrDefaultAsync(s => s.Id == id && !EF.Property<bool>(s, "Deleted"));

        if (specialty == null) return false;

        _context.Entry(specialty).Property("Deleted").CurrentValue = true;

        _context.Set<Speciality>().Update(specialty);
        await _context.SaveChangesAsync();
        return true;
    }
}