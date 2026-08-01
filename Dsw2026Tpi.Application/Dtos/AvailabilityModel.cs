using System;
using System.Collections.Generic;

namespace Dsw2026Tpi.Application.Dtos
{
  
    public class AvailabilityRequestDto
    {
        public Guid DoctorId { get; set; }
        public List<AvailabilityDayDto> Days { get; set; } = new();
    }

    public class AvailabilityDayDto
    {
        public string Day { get; set; } = string.Empty; 
        public string StartTime { get; set; } = string.Empty; 
        public string EndTime { get; set; } = string.Empty;   
    }

    
    public class DoctorAvailabilityResponseDto
    {
        public Guid Id { get; set; }
        public string Day { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }
}
