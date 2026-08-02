using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2026Tpi.Api;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2026Tpi.Tests.Integration.Appointments;

public sealed class AppointmentsApiTests : IAsyncLifetime
{
    private const string AdministratorEmail = "appointments-admin@example.com";
    private const string AdministratorPassword = "Admin1!x";
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 10, 15, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"dsw2026-appointments-{Guid.NewGuid():N}.db");
    private AppointmentsApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new AppointmentsApiFactory(_databasePath);
        _ = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PatientCanCancelAndConcurrentRebookingPreservesHistoryAsync()
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("patient-one@example.com", "12345678");

        var create = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "12345678"));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.Equal("BOOKED", appointment.Status);
        Assert.Equal(data.Speciality.Name, appointment.Specialty);
        Assert.Equal(data.Doctor.Name, appointment.Doctor);

        var active = await patient.GetFromJsonAsync<List<AppointmentModel.Response>>(
            "/api/appointments/patient?dni=12345678");
        Assert.Equal(appointment.Id, Assert.Single(active!).Id);

        var cancellation = await patient.DeleteAsync($"/api/appointments/{appointment.Id}");
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        Assert.Equal("ok", await cancellation.Content.ReadAsStringAsync());
        Assert.Empty((await patient.GetFromJsonAsync<List<AppointmentModel.Response>>(
            "/api/appointments/patient?dni=12345678"))!);

        using var secondPatient = await CreatePatientClientAsync("patient-two@example.com", "1234567");
        var rebookingResponses = await Task.WhenAll(
            patient.PostAsJsonAsync(
                "/api/appointments",
                Request(data.Doctor.Id, data.Slot.Id, "12345678")),
            secondPatient.PostAsJsonAsync(
                "/api/appointments",
                Request(data.Doctor.Id, data.Slot.Id, "1234567")));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            rebookingResponses.Select(response => response.StatusCode).Order().ToArray());

        var rebookedResponse = rebookingResponses.Single(response => response.StatusCode == HttpStatusCode.Created);
        var rebooked = (await rebookedResponse.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.NotEqual(appointment.Id, rebooked.Id);
        Assert.Equal("BOOKED", rebooked.Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var history = await context.Appointments
            .Where(candidate => candidate.AvailabilitySlotId == data.Slot.Id)
            .OrderBy(candidate => candidate.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, candidate =>
            candidate.Id == appointment.Id && candidate.Status == AppointmentStatus.CANCELLED);
        Assert.Contains(history, candidate =>
            candidate.Id == rebooked.Id && candidate.Status == AppointmentStatus.BOOKED);
    }

    [Fact]
    public async Task AppointmentForLegacyDoctorWithoutSpecialityCanBeCreatedAndRead()
    {
        var data = await CreateSlotAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            await context.Set<Doctor>().Where(doctor => doctor.Id == data.Doctor.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(doctor => doctor.SpecialityId, (Guid?)null));
        }
        using var patient = await CreatePatientClientAsync("legacy-doctor@example.com", "10234567");

        var create = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "10234567"));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.Null(appointment.SpecialtyId);
        Assert.Null(appointment.Specialty);
        Assert.Equal(appointment.Id, Assert.Single((await patient.GetFromJsonAsync<List<AppointmentModel.Response>>(
            "/api/appointments/patient?dni=10234567"))!).Id);
    }

    [Fact]
    public async Task AdministratorCanFilterByDateAndCombinedPagedCriteria()
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("patient-search@example.com", "23456789");
        Assert.Equal(
            HttpStatusCode.Created,
            (await patient.PostAsJsonAsync(
                "/api/appointments",
                Request(data.Doctor.Id, data.Slot.Id, "23456789"))).StatusCode);
        using var administrator = await CreateAdministratorClientAsync();

        var byDate = await administrator.GetFromJsonAsync<List<AppointmentModel.Response>>(
            $"/api/appointments?date={data.Slot.SlotDate:yyyy-MM-dd}");
        Assert.Contains(byDate!, item => item.AvailabilitySlotId == data.Slot.Id);

        var result = await administrator.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            $"/api/appointments/search?specialtyId={data.Speciality.Id}" +
            $"&doctorId={data.Doctor.Id}&dni=23456789&date={data.Slot.SlotDate:yyyy-MM-dd}" +
            "&pageIndex=0&pageSize=1");
        Assert.Equal(1, result!.Total);
        Assert.Single(result.Data);
        Assert.Equal(0, result.PageIndex);
        Assert.Equal(1, result.PageSize);
    }

    [Fact]
    public async Task PatientCannotReadOrCancelAnotherPatientsAppointment()
    {
        var data = await CreateSlotAsync();
        using var owner = await CreatePatientClientAsync("owner@example.com", "34567890");
        using var other = await CreatePatientClientAsync("other@example.com", "45678901");
        var create = await owner.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "34567890"));
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await other.GetAsync("/api/appointments/patient?dni=34567890")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await other.DeleteAsync($"/api/appointments/{appointment.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("123456", "Motivo válido")]
    [InlineData("123456789", "Motivo válido")]
    [InlineData("12345678", "cort")]
    public async Task CreateRejectsInvalidDniOrReason(string dni, string reason)
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("validation@example.com", "56789012");

        var response = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, dni, reason));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAcceptsFiveHundredCharacterReasonAndRejectsLongerReason()
    {
        var acceptedSlot = await CreateSlotAsync();
        var rejectedSlot = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("reason-length@example.com", "11234567");

        var accepted = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(acceptedSlot.Doctor.Id, acceptedSlot.Slot.Id, "11234567", new string('a', 500)));
        var rejected = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(rejectedSlot.Doctor.Id, rejectedSlot.Slot.Id, "11234567", new string('a', 501)));

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task CreateRejectsAPastSlotAndADoctorThatDoesNotOwnTheSlot()
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("rules@example.com", "89012345");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            await context.AvailabilitySlots
                .Where(slot => slot.Id == data.Slot.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(slot => slot.SlotDate, new DateOnly(2026, 8, 9)));
        }

        var past = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "89012345"));
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var wrongDoctor = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(Guid.NewGuid(), data.Slot.Id, "89012345"));
        Assert.Equal(HttpStatusCode.NotFound, wrongDoctor.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsCreateOneBookingAndReturnOneConflict()
    {
        var data = await CreateSlotAsync();
        using var first = await CreatePatientClientAsync("concurrent-one@example.com", "67890123");
        using var second = await CreatePatientClientAsync("concurrent-two@example.com", "78901234");

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync(
                "/api/appointments",
                Request(data.Doctor.Id, data.Slot.Id, "67890123")),
            second.PostAsJsonAsync(
                "/api/appointments",
                Request(data.Doctor.Id, data.Slot.Id, "78901234")));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order().ToArray());
        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("APPOINTMENT_CONFLICT", body.RootElement.GetProperty("errorCode").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        Assert.Equal(
            1,
            await context.Appointments.CountAsync(appointment =>
                appointment.AvailabilitySlotId == data.Slot.Id &&
                appointment.Status == AppointmentStatus.BOOKED));
    }

    [Theory]
    [InlineData(false, AppointmentStatus.BOOKED, "BOOKED")]
    [InlineData(true, AppointmentStatus.CANCELLED, "AVAILABLE")]
    public async Task AvailabilityUpdatePreservesSlotsReferencedByAppointments(
        bool cancel,
        AppointmentStatus expectedAppointmentStatus,
        string expectedSlotStatus)
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync(
            $"history-{cancel}@example.com",
            cancel ? "90123456" : "91234567");
        var create = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, cancel ? "90123456" : "91234567"));
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        if (cancel)
            Assert.Equal(HttpStatusCode.OK, (await patient.DeleteAsync($"/api/appointments/{appointment.Id}")).StatusCode);
        using var administrator = await CreateAdministratorClientAsync();

        var response = await administrator.PutAsJsonAsync(
            "/api/availabilities",
            new AvailabilityRequestDto
            {
                DoctorId = data.Doctor.Id,
                Days = [new AvailabilityDayDto { Day = "MIÉRCOLES", StartTime = "10:00", EndTime = "11:00" }]
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AVAILABILITY_HAS_APPOINTMENTS", body.RootElement.GetProperty("errorCode").GetString());
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        Assert.Equal(expectedAppointmentStatus, (await context.Appointments.FindAsync(appointment.Id))!.Status);
        Assert.Equal(expectedSlotStatus, (await context.AvailabilitySlots.FindAsync(data.Slot.Id))!.Status);
    }

    private async Task<(Speciality Speciality, Doctor Doctor, AvailabilitySlot Slot)> CreateSlotAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var speciality = new Speciality($"Especialidad {Guid.NewGuid():N}", "Especialidad para pruebas de citas.");
        var doctor = new Doctor($"Médico {Guid.NewGuid():N}", $"MP-{Guid.NewGuid():N}", speciality);
        var rule = new AvailabilityRule
        {
            DoctorId = doctor.Id,
            Doctor = doctor,
            Month = 8,
            Year = 2026,
            DayOfWeek = DayOfWeek.Tuesday.ToString(),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0)
        };
        var slot = new AvailabilitySlot
        {
            AvailabilityRule = rule,
            AvailabilityRuleId = rule.Id,
            DoctorId = doctor.Id,
            SlotDate = new DateOnly(2026, 8, 11),
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(9, 30)
        };
        context.AddRange(speciality, doctor, rule, slot);
        await context.SaveChangesAsync();
        return (speciality, doctor, slot);
    }

    private async Task<HttpClient> CreatePatientClientAsync(string email, string dni)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/patient/login",
            new LoginPatientModel.Request(email, dni));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginPatientModel.Response>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> CreateAdministratorClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/admin/login",
            new LoginAdminModel.Request(AdministratorEmail, AdministratorPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginAdminModel.Response>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static AppointmentModel.Request Request(
        Guid doctorId,
        Guid slotId,
        string dni,
        string reason = "Control anual") => new()
        {
            DoctorId = doctorId,
            AvailabilitySlotId = slotId,
            Patient = new AppointmentModel.PatientRequest { Dni = dni },
            Reason = reason
        };

    private sealed class AppointmentsApiFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var connectionString =
                $"Data Source={databasePath};Foreign Keys=True;Pooling=False;Default Timeout=30";
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["InitialAdministrator:Email"] = AdministratorEmail,
                    ["InitialAdministrator:Password"] = AdministratorPassword
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));

                // Cada prueba usa un archivo propio para ejercitar concurrencia real sin compartir estado.
                services.RemoveAll<Dsw2026TpiDbContext>();
                services.RemoveAll<DbContextOptions<Dsw2026TpiDbContext>>();
                services.AddDbContext<Dsw2026TpiDbContext>(options => options.UseSqlite(connectionString));
                services.RemoveAll<AuthenticationDbContext>();
                services.RemoveAll<DbContextOptions<AuthenticationDbContext>>();
                services.AddDbContext<AuthenticationDbContext>(options => options.UseSqlite(connectionString));
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => now;
    }
}
