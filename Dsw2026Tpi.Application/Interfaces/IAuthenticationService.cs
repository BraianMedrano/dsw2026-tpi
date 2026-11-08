using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IAuthenticationService
{
    Task<LoginAdminModel.Response> LoginAdmin(LoginAdminModel.Request request);
    Task<LoginPatientModel.Response> LoginPatient(LoginPatientModel.Request request);
}
