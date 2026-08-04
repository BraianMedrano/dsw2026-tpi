using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IAppointmentService
{
    Task<AppointmentModel.Response> CreateAsync(AppointmentModel.Request request, string patientEmail);
    Task<IReadOnlyCollection<AppointmentModel.Response>> GetActiveForPatientAsync(string dni, string patientEmail);
    Task<AppointmentModel.PagedResponse> GetHistoryForPatientAsync(
        string dni,
        string patientEmail,
        int pageIndex,
        int pageSize);
    Task CancelAsync(Guid id, string patientEmail);
    Task<IReadOnlyCollection<AppointmentModel.Response>> GetByDateAsync(DateOnly date);
    Task<AppointmentModel.AdminPagedResponse> SearchAsync(
        Guid? specialtyId,
        Guid? doctorId,
        string? dni,
        DateOnly? date,
        int pageIndex,
        int pageSize);
}
