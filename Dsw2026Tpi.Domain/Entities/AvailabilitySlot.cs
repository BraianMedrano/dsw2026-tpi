using System;

namespace Dsw2026Tpi.Domain.Entities
{
    public class AvailabilitySlot
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid AvailabilityRuleId { get; set; }
        public DateOnly Date { get; set; } 
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } = "AVAILABLE";
        public bool Deleted { get; set; } = false;
        public DateTime CreatedAt{ get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

      
        public virtual AvailabilityRule AvailabilityRule { get; set; } = null!;
    }
}