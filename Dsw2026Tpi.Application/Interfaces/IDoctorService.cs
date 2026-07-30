using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IDoctorService
{
    Task<DoctorModel.PagedResponse> GetAllAsync(int pageSize, int pageIndex, string? name = null);
    Task<DoctorModel.Response> CreateAsync(DoctorModel.Request request);
    Task UpdateAsync(Guid id, DoctorModel.Request request);
    Task DeleteAsync(Guid id);
}
