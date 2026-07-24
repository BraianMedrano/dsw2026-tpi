using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface ISpecialtyService
{
    Task<SpecialtyModel.PagedResponse> GetAllAsync(int pageSize, int pageIndex, string? name);
    Task<SpecialtyModel.Response> CreateAsync(SpecialtyModel.Request request);
    Task<bool> UpdateAsync(Guid id, SpecialtyModel.Request request);
    Task<bool> DeleteAsync(Guid id);
}