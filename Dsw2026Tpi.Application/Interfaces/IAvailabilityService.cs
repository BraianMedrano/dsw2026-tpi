using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces
{
    public interface IAvailabilityService
    {
        Task<List<DoctorAvailabilityResponseDto>> GetDoctorAvailabilityAsync(Guid doctorId);
        Task CreateAvailabilityAsync(AvailabilityRequestDto request);
        Task UpdateAvailabilityAsync(AvailabilityRequestDto request);
    }
}