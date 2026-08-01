using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers
{
    [ApiController]
    [Route("api")]
    [Authorize] 
    public class AvailabilitiesController : ControllerBase
    {
        private readonly IAvailabilityService _availabilityService;

        public AvailabilitiesController(IAvailabilityService availabilityService)
        {
            _availabilityService = availabilityService;
        }

      
        [HttpGet("doctors/{id}/availabilities")]
        [Authorize(Roles = "ADMINISTRADOR")] 
        public async Task<ActionResult<List<DoctorAvailabilityResponseDto>>> GetDoctorAvailability(Guid id)
        {
            try
            {
                var result = await _availabilityService.GetDoctorAvailabilityAsync(id);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

      
        [HttpPost("availabilities")]
        [Authorize(Roles = "ADMINISTRADOR")]
        public async Task<IActionResult> CreateAvailability([FromBody] AvailabilityRequestDto request)
        {
            try
            {
                await _availabilityService.CreateAvailabilityAsync(request);
                return StatusCode(201);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

       
        [HttpPut("availabilities")]
        [Authorize(Roles = "ADMINISTRADOR")]
        public async Task<IActionResult> UpdateAvailability([FromBody] AvailabilityRequestDto request)
        {
            try
            {
                await _availabilityService.UpdateAvailabilityAsync(request);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}