using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IAppointmentService
{
    Task<AppointmentModel.Response> CreateAsync(AppointmentModel.Request request, string patientEmail);
    Task<IReadOnlyCollection<AppointmentModel.Response>> GetActiveForPatientAsync(string dni, string patientEmail);
    Task CancelAsync(Guid id, string patientEmail);
    Task<IReadOnlyCollection<AppointmentModel.Response>> GetByDateAsync(DateOnly date);
    Task<AppointmentModel.PagedResponse> SearchAsync(
        Guid? specialtyId,
        Guid? doctorId,
        string? dni,
        DateOnly? date,
        int pageIndex,
        int pageSize);
}
