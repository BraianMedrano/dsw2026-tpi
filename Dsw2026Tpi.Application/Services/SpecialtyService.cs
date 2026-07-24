using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
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
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalizedName = name.Trim();
            query = query.Where(s => s.Name.Contains(normalizedName));
        }

        var total = await query.CountAsync();

        var data = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(s => new SpecialtyModel.Response(s.Id, s.Name, s.Description))
            .ToListAsync();

        return new SpecialtyModel.PagedResponse(pageSize, pageIndex, data, total);
    }

    public async Task<SpecialtyModel.Response> GetByIdAsync(Guid id)
    {
        var specialty = await _context.Set<Speciality>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new EntityNotFoundException("Especialidad");

        return new SpecialtyModel.Response(specialty.Id, specialty.Name, specialty.Description);
    }

    public async Task<SpecialtyModel.Response> CreateAsync(SpecialtyModel.Request request)
    {
        var specialty = new Speciality(request.Name, request.Description);

        _context.Set<Speciality>().Add(specialty);
        await _context.SaveChangesAsync();

        return new SpecialtyModel.Response(specialty.Id, specialty.Name, specialty.Description);
    }

    public async Task UpdateAsync(Guid id, SpecialtyModel.Request request)
    {
        var specialty = await _context.Set<Speciality>()
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new EntityNotFoundException("Especialidad");

        _context.Entry(specialty).CurrentValues.SetValues(new
        {
            Name = request.Name,
            Description = request.Description
        });

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var specialty = await _context.Set<Speciality>()
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new EntityNotFoundException("Especialidad");

        specialty.MarkAsDeleted();
        await _context.SaveChangesAsync();
    }
}
