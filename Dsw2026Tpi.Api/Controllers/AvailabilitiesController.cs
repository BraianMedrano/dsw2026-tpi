using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers
{
    [ApiController]
    [Route("api")]
    public class AvailabilitiesController : ControllerBase
    {
        private readonly IAvailabilityService _availabilityService;

        public AvailabilitiesController(IAvailabilityService availabilityService)
        {
            _availabilityService = availabilityService;
        }

      
        [HttpGet("doctors/{id}/availabilities")]
        [Authorize(Policy = Policies.AdminPolicy)]
        public async Task<ActionResult<List<DoctorAvailabilityResponseDto>>> GetDoctorAvailability(Guid id)
        {
            var result = await _availabilityService.GetDoctorAvailabilityAsync(id);
            return Ok(result);
        }

        [HttpGet("availability-slots")]
        [Authorize(Policy = Policies.PatientPolicy)]
        public async Task<ActionResult<IReadOnlyCollection<AvailabilitySlotModel.Response>>> GetAvailableSlots(
            [FromQuery] AvailabilitySlotModel.Query query)
        {
            var result = await _availabilityService.GetAvailableSlotsAsync(
                query.SpecialtyId,
                query.DoctorId,
                query.Date);
            return Ok(result);
        }

      
        [HttpPost("availabilities")]
        [Authorize(Policy = Policies.AdminPolicy)]
        public async Task<ActionResult<List<DoctorAvailabilityResponseDto>>> CreateAvailability([FromBody] AvailabilityRequestDto request)
        {
            var result = await _availabilityService.CreateAvailabilityAsync(request);
            return StatusCode(StatusCodes.Status201Created, result);
        }

       
        [HttpPut("availabilities")]
        [Authorize(Policy = Policies.AdminPolicy)]
        public async Task<ActionResult<List<DoctorAvailabilityResponseDto>>> UpdateAvailability([FromBody] AvailabilityRequestDto request)
        {
            var result = await _availabilityService.UpdateAvailabilityAsync(request);
            return Ok(result);
        }
    }
}
