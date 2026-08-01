using System;
using System.Collections.Generic;

namespace Dsw2026Tpi.Domain.Entities
{
    public class AvailabilityRule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid DoctorId { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public string DayOfWeek { get; set; } = string.Empty; 
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool Deleted { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

       
        public virtual Doctor Doctor { get; set; } = null!;

       
        public virtual ICollection<AvailabilitySlot> Slots { get; set; } = new List<AvailabilitySlot>();
    }
}