using System.Net;
using System.Text.Json;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Tests.Integration.Doctors;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2026Tpi.Tests.Integration.AvailabilitySlots;

public sealed class AvailabilitySlotsApiTests : IClassFixture<DoctorsApiFactory>
{
    private static readonly DateOnly FutureDate = new(2026, 8, 11);
    private readonly DoctorsApiFactory _factory;

    public AvailabilitySlotsApiTests(DoctorsApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Administrator, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Patient, HttpStatusCode.OK)]
    public async Task Get_EnforcesThePatientPolicy(string? role, HttpStatusCode expected)
    {
        using var client = _factory.CreateClientForRole(role);

        var response = await client.GetAsync("/api/availability-slots");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsTheExactPatientShapeInDeterministicChronologicalOrder()
    {
        var firstDoctorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondDoctorId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var thirdDoctorId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var firstSlotId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var secondSlotId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var lastSlotId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var specialtyId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var specialty = new Speciality("Clínica médica", "Atención clínica integral", specialtyId);
            var thirdDoctor = new Doctor("Carla", "MP-300", specialty, thirdDoctorId);
            var secondDoctor = new Doctor("Beatriz", "MP-200", specialty, secondDoctorId);
            var firstDoctor = new Doctor("Ana", "MP-100", specialty, firstDoctorId);
            AddCandidate(context, thirdDoctor, new TimeOnly(9, 30), lastSlotId);
            AddCandidate(context, secondDoctor, new TimeOnly(9, 0), secondSlotId);
            AddCandidate(context, firstDoctor, new TimeOnly(9, 0), firstSlotId);
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateClientForRole(Roles.Patient);
        var response = await client.GetAsync($"/api/availability-slots?specialtyId={specialtyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = payload.RootElement.EnumerateArray().ToArray();
        Assert.Equal([firstSlotId, secondSlotId, lastSlotId], items.Select(SlotId).ToArray());

        var item = items[0];
        Assert.Equal(
            ["availabilitySlotId", "date", "startTime", "endTime", "doctor", "specialty"],
            item.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("2026-08-11", item.GetProperty("date").GetString());
        Assert.Equal("09:00", item.GetProperty("startTime").GetString());
        Assert.Equal("09:30", item.GetProperty("endTime").GetString());
        Assert.Equal(
            ["doctorId", "name"],
            item.GetProperty("doctor").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(firstDoctorId, item.GetProperty("doctor").GetProperty("doctorId").GetGuid());
        Assert.Equal("Ana", item.GetProperty("doctor").GetProperty("name").GetString());
        Assert.Equal(
            ["specialtyId", "name"],
            item.GetProperty("specialty").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(specialtyId, item.GetProperty("specialty").GetProperty("specialtyId").GetGuid());
        Assert.Equal("Clínica médica", item.GetProperty("specialty").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_FiltersBySpecialtyDoctorAndExactCalendarDate()
    {
        var filterDate = new DateOnly(2026, 8, 21);
        var firstSpecialty = new Speciality("Cardiología", "Atención cardiovascular");
        var secondSpecialty = new Speciality("Neurología", "Atención neurológica");
        var firstDoctor = new Doctor("Doctor uno", "MP-301", firstSpecialty);
        var secondDoctor = new Doctor("Doctor dos", "MP-302", firstSpecialty);
        var thirdDoctor = new Doctor("Doctor tres", "MP-303", secondSpecialty);
        var firstSlot = Guid.NewGuid();
        var secondSlot = Guid.NewGuid();
        var thirdSlot = Guid.NewGuid();
        var otherDateSlot = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            AddCandidate(context, firstDoctor, new TimeOnly(9, 0), firstSlot, date: filterDate);
            AddCandidate(context, secondDoctor, new TimeOnly(10, 0), secondSlot, date: filterDate);
            AddCandidate(context, thirdDoctor, new TimeOnly(11, 0), thirdSlot, date: filterDate);
            AddCandidate(context, firstDoctor, new TimeOnly(12, 0), otherDateSlot, date: filterDate.AddDays(1));
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateClientForRole(Roles.Patient);

        Assert.Equal(
            [firstSlot, secondSlot],
            await GetSlotIdsAsync(client, $"?specialtyId={firstSpecialty.Id}&date=2026-08-21"));
        Assert.Equal(
            [firstSlot, otherDateSlot],
            await GetSlotIdsAsync(client, $"?doctorId={firstDoctor.Id}"));
        Assert.Equal(
            [firstSlot, secondSlot, thirdSlot],
            await GetSlotIdsAsync(client, "?date=2026-08-21"));
    }

    [Fact]
    public async Task Get_ExcludesPastBookedDeletedAndInactiveCandidates()
    {
        var expected = Guid.NewGuid();
        var excluded = new List<Guid>();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            AddCandidate(context, ActiveDoctor("Disponible"), new TimeOnly(9, 0), expected);
            excluded.Add(AddCandidate(context, ActiveDoctor("Reservado"), new TimeOnly(9, 0), status: "BOOKED"));
            excluded.Add(AddCandidate(context, ActiveDoctor("Slot eliminado"), new TimeOnly(9, 0), slotDeleted: true));
            excluded.Add(AddCandidate(context, ActiveDoctor("Regla eliminada"), new TimeOnly(9, 0), ruleDeleted: true));
            excluded.Add(AddCandidate(context, InactiveDoctor("Médico inactivo"), new TimeOnly(9, 0)));
            excluded.Add(AddCandidate(context, DoctorWithDeletedSpecialty("Especialidad eliminada"), new TimeOnly(9, 0)));
            excluded.Add(AddCandidate(context, ActiveDoctor("Fecha pasada"), new TimeOnly(9, 0), date: FutureDate.AddDays(-2)));
            excluded.Add(AddCandidate(
                context,
                ActiveDoctor("Hora pasada"),
                new TimeOnly(10, 0),
                date: DateOnly.FromDateTime(DoctorsApiFactory.AvailabilityNow.DateTime)));
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateClientForRole(Roles.Patient);

        var actual = await GetSlotIdsAsync(client);
        Assert.Contains(expected, actual);
        Assert.All(excluded, id => Assert.DoesNotContain(id, actual));
    }

    private static async Task<Guid[]> GetSlotIdsAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/api/availability-slots{query}");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.EnumerateArray().Select(SlotId).ToArray();
    }

    private static Guid SlotId(JsonElement item) => item.GetProperty("availabilitySlotId").GetGuid();

    private static Doctor ActiveDoctor(string name) => new(
        name,
        $"MP-{Guid.NewGuid():N}",
        new Speciality($"Especialidad {Guid.NewGuid():N}", "Descripción válida para pruebas"));

    private static Doctor InactiveDoctor(string name)
    {
        var doctor = ActiveDoctor(name);
        doctor.MarkAsDeleted();
        return doctor;
    }

    private static Doctor DoctorWithDeletedSpecialty(string name)
    {
        var specialty = new Speciality(
            $"Especialidad {Guid.NewGuid():N}",
            "Descripción válida para pruebas");
        specialty.MarkAsDeleted();
        return new Doctor(name, $"MP-{Guid.NewGuid():N}", specialty);
    }

    private static Guid AddCandidate(
        Dsw2026TpiDbContext context,
        Doctor doctor,
        TimeOnly start,
        Guid? slotId = null,
        string status = "AVAILABLE",
        bool slotDeleted = false,
        bool ruleDeleted = false,
        DateOnly? date = null)
    {
        var slotDate = date ?? FutureDate;
        var rule = new AvailabilityRule
        {
            Doctor = doctor,
            DoctorId = doctor.Id,
            Year = slotDate.Year,
            Month = slotDate.Month,
            DayOfWeek = slotDate.DayOfWeek.ToString(),
            StartTime = start,
            EndTime = start.AddMinutes(30),
            Deleted = ruleDeleted
        };
        var slot = new AvailabilitySlot
        {
            Id = slotId ?? Guid.NewGuid(),
            AvailabilityRule = rule,
            AvailabilityRuleId = rule.Id,
            DoctorId = doctor.Id,
            SlotDate = slotDate,
            StartTime = start,
            EndTime = start.AddMinutes(30),
            Status = status,
            Deleted = slotDeleted
        };
        context.AvailabilitySlots.Add(slot);
        return slot.Id;
    }
}
