using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

[ApiController]
[Route("api/appointments")]
public sealed class AppointmentsController(IAppointmentService appointmentService) : ControllerBase
{
    private readonly IAppointmentService _appointmentService = appointmentService;

    [HttpPost]
    [Authorize(Policy = Policies.PatientPolicy)]
    public async Task<ActionResult<AppointmentModel.Response>> Create(AppointmentModel.Request request)
    {
        var result = await _appointmentService.CreateAsync(request, PatientEmail());
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("patient")]
    [Authorize(Policy = Policies.PatientPolicy)]
    public async Task<ActionResult<IReadOnlyCollection<AppointmentModel.Response>>> GetForPatient([FromQuery] string dni)
    {
        return Ok(await _appointmentService.GetActiveForPatientAsync(dni, PatientEmail()));
    }

    [HttpGet("patient/history")]
    [Authorize(Policy = Policies.PatientPolicy)]
    public async Task<ActionResult<AppointmentModel.PagedResponse>> GetHistoryForPatient(
        [FromQuery] AppointmentModel.HistoryQuery query)
    {
        return Ok(await _appointmentService.GetHistoryForPatientAsync(
            query.Dni,
            PatientEmail(),
            query.PageIndex,
            query.PageSize));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.PatientPolicy)]
    public async Task<ActionResult<string>> Cancel(Guid id)
    {
        await _appointmentService.CancelAsync(id, PatientEmail());
        return Ok("ok");
    }

    [HttpGet]
    [Authorize(Policy = Policies.AdminPolicy)]
    public async Task<ActionResult<IReadOnlyCollection<AppointmentModel.Response>>> GetByDate([FromQuery] DateOnly date)
    {
        return Ok(await _appointmentService.GetByDateAsync(date));
    }

    [HttpGet("search")]
    [Authorize(Policy = Policies.AdminPolicy)]
    public async Task<ActionResult<AppointmentModel.PagedResponse>> Search(
        [FromQuery] Guid? specialtyId,
        [FromQuery] Guid? doctorId,
        [FromQuery] string? dni,
        [FromQuery] DateOnly? date,
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 10)
    {
        return Ok(await _appointmentService.SearchAsync(
            specialtyId,
            doctorId,
            dni,
            date,
            pageIndex,
            pageSize));
    }

    private string PatientEmail() =>
        User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("El token autenticado no contiene la identidad del paciente.");
}
