using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

[Route("api/doctors")]
[Authorize(Policy = Policies.AdminPolicy)]
public class DoctorController : AppController
{
    private readonly IDoctorService _service;

    public DoctorController(IDoctorService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DoctorModel.Query query)
    {
        var doctors = await _service.GetAllAsync(query.PageSize, query.PageIndex, query.Name);
        return Ok(doctors);
    }

    // F03: GET /api/doctors/{id}. Lo usa el frontend para cargar el formulario
    // de edición. Sigue el mismo patrón que SpecialtiesController.GetById:
    // 404 (ENTITY_NOTFOUND) si no existe y 403 para pacientes, por la política
    // de administrador de la clase.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var doctor = await _service.GetByIdAsync(id);
        return Ok(doctor);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DoctorModel.Request request)
    {
        var created = await _service.CreateAsync(request);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DoctorModel.Request request)
    {
        var updated = await _service.UpdateAsync(id, request);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok("ok");
    }
}
