using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Tests.Integration.Doctors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2026Tpi.Tests.Integration.Availabilities;

public sealed class AvailabilitiesApiTests : IClassFixture<DoctorsApiFactory>
{
    private readonly DoctorsApiFactory _factory;

    public AvailabilitiesApiTests(DoctorsApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Administrator, HttpStatusCode.OK)]
    public async Task Get_EnforcesTheRealAdministratorPolicy(string? role, HttpStatusCode expected)
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateClientForRole(role);

        var response = await client.GetAsync($"/api/doctors/{doctor.Id}/availabilities");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsEmptyCollectionForActiveDoctorWithoutConfiguration()
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();

        var response = await client.GetAsync($"/api/doctors/{doctor.Id}/availabilities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
    }

    [Fact]
    public async Task Post_ReturnsCreatedDtoWithRuleIdAndPersistsThirtyMinuteSlots()
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();
        var request = RequestFor(doctor.Id, "LUNES", "09:00", "11:00");

        var response = await client.PostAsJsonAsync("/api/availabilities", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("LUNES", created.Day);
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var slots = await context.AvailabilitySlots
            .Where(slot => slot.AvailabilityRuleId == created.Id)
            .OrderBy(slot => slot.SlotDate)
            .ThenBy(slot => slot.StartTime)
            .ToListAsync();
        Assert.Equal(13, slots.Count);
        Assert.Equal(new DateOnly(2026, 8, 10), slots[0].SlotDate);
        Assert.Equal(new TimeOnly(10, 30), slots[0].StartTime);
        Assert.Equal(
            [new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31)],
            slots.Select(slot => slot.SlotDate).Distinct().ToList());
        Assert.All(slots, slot => Assert.Equal(slot.StartTime.AddMinutes(30), slot.EndTime));
    }

    [Fact]
    public async Task Post_MapsUniqueRuleConstraintToConflictWhenExistingRuleHasNoSlots()
    {
        var doctor = await CreateActiveDoctorAsync();
        var date = new DateOnly(2026, 8, 12);
        var rule = new AvailabilityRule
        {
            DoctorId = doctor.Id,
            Year = date.Year,
            Month = date.Month,
            DayOfWeek = date.DayOfWeek.ToString(),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Deleted = true
        };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            context.AvailabilityRules.Add(rule);
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateAdministratorClient();
        var response = await client.PostAsJsonAsync(
            "/api/availabilities",
            RequestFor(doctor.Id, ToSpanishDay(date.DayOfWeek), "09:00", "10:00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AVAILABILITY_DUPLICATE_DAY", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Post_RejectsASecondNonOverlappingRangeForTheSameDay()
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, "LUNES", "09:00", "10:00"))).StatusCode);

        var response = await client.PostAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, "LUNES", "14:00", "15:00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AVAILABILITY_DUPLICATE_DAY", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("INVALIDO", "09:00", "10:00")]
    [InlineData("LUNES", "9:00", "10:00")]
    [InlineData("LUNES", "10:00", "10:00")]
    [InlineData("LUNES", "09:15", "10:00")]
    public async Task Post_RejectsInvalidDayOrRange(string day, string start, string end)
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, day, start, end));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AVAILABILITY_VALIDATION", body.RootElement.GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData("MIÉRCOLES")]
    [InlineData("SÁBADO")]
    public async Task Post_ReturnsSpanishDaysWithAccents(string day)
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();

        var response = await client.PostAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, day, "09:00", "10:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
        Assert.Equal(day, created.Day);
    }

    [Fact]
    public async Task Put_ReplacesEntireCurrentMonthConfiguration()
    {
        var doctor = await CreateActiveDoctorAsync();
        using var client = _factory.CreateAdministratorClient();
        await client.PostAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, "LUNES", "09:00", "10:00"));

        var response = await client.PutAsJsonAsync("/api/availabilities", RequestFor(doctor.Id, "MARTES", "10:00", "11:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
        Assert.Equal("MARTES", updated.Day);
        using var get = await client.GetAsync($"/api/doctors/{doctor.Id}/availabilities");
        var current = Assert.Single((await get.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
        Assert.Equal("MARTES", current.Day);
    }

    [Fact]
    public async Task Put_ReplacesOnlyFutureUnreservedSlotsAndPreservesHistory()
    {
        var doctor = await CreateActiveDoctorAsync();
        var rule = new AvailabilityRule
        {
            DoctorId = doctor.Id,
            Year = 2026,
            Month = 8,
            DayOfWeek = DayOfWeek.Monday.ToString(),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0)
        };
        var past = Slot(rule, new DateOnly(2026, 8, 10), new TimeOnly(9, 0));
        var futureAvailable = Slot(rule, new DateOnly(2026, 8, 17), new TimeOnly(9, 0));
        var futureBooked = Slot(rule, new DateOnly(2026, 8, 17), new TimeOnly(9, 30), "BOOKED");
        var appointment = new Appointment
        {
            DoctorId = doctor.Id,
            AvailabilitySlotId = futureBooked.Id,
            PatientDni = "23456789",
            Reason = "Control anual",
            Status = AppointmentStatus.BOOKED
        };
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            context.AddRange(rule, past, futureAvailable, futureBooked, appointment);
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateAdministratorClient();
        var response = await client.PutAsJsonAsync(
            "/api/availabilities",
            RequestFor(doctor.Id, "MARTES", "10:00", "11:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replacement = Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
        Assert.Equal("MARTES", replacement.Day);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        Assert.NotNull(await verification.AvailabilitySlots.FindAsync(past.Id));
        Assert.Null(await verification.AvailabilitySlots.FindAsync(futureAvailable.Id));
        var preservedBookedSlot = (await verification.AvailabilitySlots.FindAsync(futureBooked.Id))!;
        Assert.Equal("BOOKED", preservedBookedSlot.Status);
        Assert.True(preservedBookedSlot.Deleted);
        var persistedAppointment = await verification.Appointments.FindAsync(appointment.Id);
        Assert.NotNull(persistedAppointment);
        Assert.Equal(futureBooked.Id, persistedAppointment.AvailabilitySlotId);
        Assert.Equal(AppointmentStatus.BOOKED, persistedAppointment.Status);
        Assert.True((await verification.AvailabilityRules.FindAsync(rule.Id))!.Deleted);
        Assert.Contains(
            await verification.AvailabilitySlots.Where(slot => slot.DoctorId == doctor.Id).ToListAsync(),
            slot => slot.SlotDate > DateOnly.FromDateTime(DoctorsApiFactory.AvailabilityNow.DateTime) &&
                    slot.StartTime == new TimeOnly(10, 0) && slot.Status == "AVAILABLE");
    }

    [Fact]
    public async Task Post_ExcludesFixedHolidayAndGeneratesTheExactRemainingSlots()
    {
        var holiday = new DateOnly(2026, 8, 17);
        await WithCalendarAsync("{ \"dates\": [\"2026-08-17\"] }", async () =>
        {
            var doctor = await CreateActiveDoctorAsync();
            using var client = _factory.CreateAdministratorClient();
            var response = await client.PostAsJsonAsync(
                "/api/availabilities",
                RequestFor(doctor.Id, "LUNES", "09:00", "11:00"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var rule = Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponseDto>>())!);
            await using var scope = _factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var slots = await context.AvailabilitySlots
                .Where(slot => slot.AvailabilityRuleId == rule.Id)
                .OrderBy(slot => slot.SlotDate)
                .ThenBy(slot => slot.StartTime)
                .ToListAsync();

            Assert.Equal(9, slots.Count);
            Assert.DoesNotContain(slots, slot => slot.SlotDate == holiday);
            Assert.Equal(
                [new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31)],
                slots.Select(slot => slot.SlotDate).Distinct().ToList());
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ invalid json")]
    [InlineData("{ \"dates\": [\"2026-99-99\"] }")]
    [InlineData("{ \"dates\": null }")]
    public async Task Post_ReturnsUniform503WhenHolidayCalendarIsInvalid(string? content)
    {
        await WithCalendarAsync(content, async () =>
        {
            var doctor = await CreateActiveDoctorAsync();
            using var client = _factory.CreateAdministratorClient();

            var response = await client.PostAsJsonAsync(
                "/api/availabilities",
                RequestFor(doctor.Id, "LUNES", "09:00", "10:00"));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("AVAILABILITY_CALENDAR_UNAVAILABLE", body.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal("El calendario de días no laborables no está disponible o es inválido.", body.RootElement.GetProperty("message").GetString());
        });
    }

    private static AvailabilityRequestDto RequestFor(Guid doctorId, string day, string start, string end) => new()
    {
        DoctorId = doctorId,
        Days = [new AvailabilityDayDto { Day = day, StartTime = start, EndTime = end }]
    };

    private static AvailabilitySlot Slot(
        AvailabilityRule rule,
        DateOnly date,
        TimeOnly start,
        string status = "AVAILABLE") => new()
    {
        AvailabilityRule = rule,
        AvailabilityRuleId = rule.Id,
        DoctorId = rule.DoctorId,
        SlotDate = date,
        StartTime = start,
        EndTime = start.AddMinutes(30),
        Status = status
    };

    private static string ToSpanishDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "LUNES", DayOfWeek.Tuesday => "MARTES", DayOfWeek.Wednesday => "MIÉRCOLES",
        DayOfWeek.Thursday => "JUEVES", DayOfWeek.Friday => "VIERNES", DayOfWeek.Saturday => "SÁBADO", _ => "DOMINGO"
    };

    private static async Task WithCalendarAsync(string? content, Func<Task> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"availability-calendar-{Guid.NewGuid():N}.json");
        if (content is not null)
            await File.WriteAllTextAsync(path, content);
        var previousPath = Environment.GetEnvironmentVariable("AVAILABILITY_NON_WORKING_DAYS_PATH");
        Environment.SetEnvironmentVariable("AVAILABILITY_NON_WORKING_DAYS_PATH", path);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AVAILABILITY_NON_WORKING_DAYS_PATH", previousPath);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private async Task<Doctor> CreateActiveDoctorAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var specialty = new Speciality($"Especialidad {Guid.NewGuid():N}", "Especialidad creada para Availability.");
        var doctor = new Doctor($"Médico {Guid.NewGuid():N}", $"MP-{Guid.NewGuid():N}", specialty);
        context.AddRange(specialty, doctor);
        await context.SaveChangesAsync();
        return doctor;
    }
}
