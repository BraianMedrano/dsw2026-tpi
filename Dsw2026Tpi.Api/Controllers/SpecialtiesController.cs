using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

[ApiController]
[Route("api/specialties")]
[Authorize(Roles = "ADMINISTRADOR")]
[AllowAnonymous] //despues eliminar solamente esta por test de endpoint
public class SpecialtiesController : ControllerBase
{
    private readonly ISpecialtyService _specialtyService;

    public SpecialtiesController(ISpecialtyService specialtyService)
    {
        _specialtyService = specialtyService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int pageSize = 10,
        [FromQuery] int pageIndex = 0,
        [FromQuery] string? name = null)
    {
        if (!string.IsNullOrEmpty(name) && (name.Length < 3 || name.Length > 100))
        {
            return BadRequest(new
            {
                errorCode = "INVALID_QUERY_PARAM",
                message = "El parámetro name debe tener entre 3 y 100 caracteres."
            });
        }

        var result = await _specialtyService.GetAllAsync(pageSize, pageIndex, name);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SpecialtyModel.Request request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _specialtyService.CreateAsync(request);
        return CreatedAtAction(nameof(GetAll), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SpecialtyModel.Request request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _specialtyService.UpdateAsync(id, request);
        if (!updated)
        {
            return NotFound(new
            {
                errorCode = "SPECIALTY_NOT_FOUND",
                message = "La especialidad no existe o fue eliminada."
            });
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _specialtyService.DeleteAsync(id);
        if (!deleted)
        {
            return NotFound(new
            {
                errorCode = "SPECIALTY_NOT_FOUND",
                message = "La especialidad no existe o fue eliminada."
            });
        }

        return NoContent();
    }
}