using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dsw2026Tpi.Application.Services
{
    public class AvailabilityService : IAvailabilityService
    {
        private readonly Dsw2026TpiDbContext _context;

        public AvailabilityService(Dsw2026TpiDbContext context)
        {
            _context = context;
        }

        public async Task<List<DoctorAvailabilityResponseDto>> GetDoctorAvailabilityAsync(Guid doctorId)
        {
            
            var doctorExists = await _context.Set<Doctor>().AnyAsync(d => d.Id == doctorId && !d.IsActive);
            if (!doctorExists)
            {
                throw new KeyNotFoundException("El médico especificado no existe o está eliminado.");
            }

            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

        
            var rules = await _context.AvailabilityRules
                .Where(r => r.DoctorId == doctorId && r.Month == currentMonth && r.Year == currentYear && !r.Deleted)
                .ToListAsync();

            return rules.Select(r => new DoctorAvailabilityResponseDto
            {
                Day = r.DayOfWeek,
                StartTime = r.StartTime.ToString("HH:mm"),
                EndTime = r.EndTime.ToString("HH:mm")
            }).ToList();
        }

        public async Task CreateAvailabilityAsync(AvailabilityRequestDto request)
        {
       
            var doctorExists = await _context.Set<Doctor>().AnyAsync(d => d.Id == request.DoctorId && !d.IsActive);
            if (!doctorExists)
            {
                throw new KeyNotFoundException("El médico especificado no existe.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var now = DateTime.Now;
                int currentMonth = now.Month;
                int currentYear = now.Year;

                foreach (var dayDto in request.Days)
                {
                    if (!TimeOnly.TryParse(dayDto.StartTime, out var startTime) ||
                        !TimeOnly.TryParse(dayDto.EndTime, out var endTime))
                    {
                        throw new ArgumentException("Formato de hora inválido. Use HH:mm.");
                    }

                   
                    if (startTime >= endTime)
                    {
                        throw new InvalidOperationException("El horario de inicio debe ser estrictamente anterior al horario de fin.");
                    }

                  
                    var rule = new AvailabilityRule
                    {
                        DoctorId = request.DoctorId,
                        Month = currentMonth,
                        Year = currentYear,
                        DayOfWeek = dayDto.Day.ToUpper(),
                        StartTime = startTime,
                        EndTime = endTime
                    };

                    _context.AvailabilityRules.Add(rule);

                  
                    GenerateSlotsForMonth(rule, currentYear, currentMonth, now);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task UpdateAvailabilityAsync(AvailabilityRequestDto request)
        {
           
            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var existingRules = await _context.AvailabilityRules
                    .Where(r => r.DoctorId == request.DoctorId && r.Month == currentMonth && r.Year == currentYear)
                    .ToListAsync();

              
                _context.AvailabilityRules.RemoveRange(existingRules);
                await _context.SaveChangesAsync();

              
                await CreateAvailabilityAsync(request);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private void GenerateSlotsForMonth(AvailabilityRule rule, int year, int month, DateTime referenceDate)
        {
        
            if (!Enum.TryParse<DayOfWeek>(MapDayNameToDayOfWeek(rule.DayOfWeek), true, out var targetDayOfWeek))
            {
                return;
            }

        
            int daysInMonth = DateTime.DaysInMonth(year, month);
            for (int day = referenceDate.Day; day <= daysInMonth; day++)
            {
                var date = new DateOnly(year, month, day);
                if (date.DayOfWeek == targetDayOfWeek)
                {
                  
                    var currentSlotStart = rule.StartTime;
                    while (currentSlotStart.AddMinutes(30) <= rule.EndTime)
                    {
                        var currentSlotEnd = currentSlotStart.AddMinutes(30);

                   
                        if (date == DateOnly.FromDateTime(referenceDate) && currentSlotStart <= TimeOnly.FromDateTime(referenceDate))
                        {
                            currentSlotStart = currentSlotEnd;
                            continue;
                        }

                        var slot = new AvailabilitySlot
                        {
                            AvailabilityRuleId = rule.Id,
                            Date = date,
                            StartTime = currentSlotStart,
                            EndTime = currentSlotEnd,
                            Status = "AVAILABLE"
                        };

                        rule.Slots.Add(slot);
                        currentSlotStart = currentSlotEnd;
                    }
                }
            }
        }

        private string MapDayNameToDayOfWeek(string dayName)
        {
            return dayName.ToUpper() switch
            {
                "LUNES" => "Monday",
                "MARTES" => "Tuesday",
                "MIÉRCOLES" or "MIERCOLES" => "Wednesday",
                "JUEVES" => "Thursday",
                "VIERNES" => "Friday",
                "SÁBADO" or "SABADO" => "Saturday",
                "DOMINGO" => "Sunday",
                _ => string.Empty
            };
        }
    }
}