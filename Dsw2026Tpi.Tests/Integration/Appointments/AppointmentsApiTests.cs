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

        var response = await administrator.GetAsync(
            $"/api/appointments/search?specialtyId={data.Speciality.Id}" +
            $"&doctorId={data.Doctor.Id}&dni=23456789&date={data.Slot.SlotDate:yyyy-MM-dd}" +
            "&pageIndex=0&pageSize=1");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(["pageIndex", "pageSize", "total", "data"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(0, root.GetProperty("pageIndex").GetInt32());
        Assert.Equal(1, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, root.GetProperty("total").GetInt32());

        var appointment = Assert.Single(root.GetProperty("data").EnumerateArray());
        Assert.Equal(["appointmentsId", "appointmentsStatus", "patient", "doctor"], appointment.EnumerateObject().Select(property => property.Name));
        Assert.Equal("BOOKED", appointment.GetProperty("appointmentsStatus").GetString());
        var patientData = appointment.GetProperty("patient");
        Assert.Equal(["dni", "fullName"], patientData.EnumerateObject().Select(property => property.Name));
        Assert.Equal(23456789, patientData.GetProperty("dni").GetInt64());
        Assert.Equal(string.Empty, patientData.GetProperty("fullName").GetString());
        var doctorData = appointment.GetProperty("doctor");
        Assert.Equal(data.Doctor.Id, doctorData.GetProperty("doctorId").GetGuid());
        Assert.Equal(data.Doctor.Name, doctorData.GetProperty("name").GetString());
        var specialty = doctorData.GetProperty("specialty");
        Assert.Equal(data.Speciality.Id, specialty.GetProperty("specialtyId").GetGuid());
        Assert.Equal(data.Speciality.Name, specialty.GetProperty("name").GetString());
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

    [Fact]
    public async Task PatientCanReadCancelledAppointmentFromHistory()
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("history-owner@example.com", "13572468");
        var create = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "13572468"));
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.Equal(HttpStatusCode.OK, (await patient.DeleteAsync($"/api/appointments/{appointment.Id}")).StatusCode);

        var response = await patient.GetAsync(
            "/api/appointments/patient/history?dni=13572468&pageIndex=0&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<AppointmentModel.PagedResponse>())!;
        Assert.Equal(1, history.Total);
        var historicalAppointment = Assert.Single(history.Data);
        Assert.Equal(appointment.Id, historicalAppointment.Id);
        Assert.Equal("CANCELLED", historicalAppointment.Status);
    }

    [Fact]
    public async Task ActiveAndHistoricalEndpointsKeepBookedAndCancelledAppointmentsSeparate()
    {
        using var patient = await CreatePatientClientAsync("history-separation@example.com", "24681357");
        var booked = await AddAppointmentAsync("24681357", AppointmentStatus.BOOKED);
        var cancelled = await AddAppointmentAsync("24681357", AppointmentStatus.CANCELLED);

        var active = (await patient.GetFromJsonAsync<List<AppointmentModel.Response>>(
            "/api/appointments/patient?dni=24681357"))!;
        var history = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=24681357"))!;

        Assert.Equal(booked.Id, Assert.Single(active).Id);
        Assert.Equal(cancelled.Id, Assert.Single(history.Data).Id);
        Assert.DoesNotContain(history.Data, appointment => appointment.Status == "BOOKED");
    }

    [Fact]
    public async Task HistoryIncludesEveryExplicitHistoricalStatusAndSupportsLegacyDoctor()
    {
        using var patient = await CreatePatientClientAsync("history-statuses@example.com", "35792468");
        var cancelled = await AddAppointmentAsync("35792468", AppointmentStatus.CANCELLED);
        var attended = await AddAppointmentAsync("35792468", AppointmentStatus.ATTENDED);
        var noShow = await AddAppointmentAsync("35792468", AppointmentStatus.NO_SHOW, legacyDoctor: true);
        _ = await AddAppointmentAsync("35792468", AppointmentStatus.BOOKED);

        var history = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=35792468&pageSize=10"))!;

        Assert.Equal(3, history.Total);
        Assert.Equal(
            ["ATTENDED", "CANCELLED", "NO_SHOW"],
            history.Data.Select(appointment => appointment.Status).Order().ToArray());
        Assert.DoesNotContain(history.Data, appointment => appointment.Status == "BOOKED");
        var legacy = Assert.Single(history.Data, appointment => appointment.Id == noShow.Id);
        Assert.Null(legacy.SpecialtyId);
        Assert.Null(legacy.Specialty);
        Assert.Contains(history.Data, appointment => appointment.Id == cancelled.Id);
        Assert.Contains(history.Data, appointment => appointment.Id == attended.Id);
    }

    [Fact]
    public async Task HistoryProtectsPatientPrivacyAndPreservesNotFoundBehavior()
    {
        using var owner = await CreatePatientClientAsync("history-private-owner@example.com", "46813579");
        using var other = await CreatePatientClientAsync("history-private-other@example.com", "57924681");
        var appointment = await AddAppointmentAsync("46813579", AppointmentStatus.CANCELLED);
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var doctor = (await context.Set<Doctor>().FindAsync(appointment.DoctorId))!;

        var forbidden = await other.GetAsync(
            "/api/appointments/patient/history?dni=46813579");
        var notFound = await other.GetAsync(
            "/api/appointments/patient/history?dni=68035791");

        await AssertErrorContractAsync(forbidden, HttpStatusCode.Forbidden, "AUTHORIZATION_FAILED", false);
        var forbiddenBody = await forbidden.Content.ReadAsStringAsync();
        Assert.DoesNotContain(appointment.Id.ToString(), forbiddenBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appointment.Reason, forbiddenBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appointment.DoctorId.ToString(), forbiddenBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(doctor.Name, forbiddenBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appointment.AvailabilitySlotId.ToString(), forbiddenBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appointment.PatientDni, forbiddenBody, StringComparison.OrdinalIgnoreCase);
        await AssertErrorContractAsync(notFound, HttpStatusCode.NotFound, "ENTITY_NOTFOUND", false);
    }

    [Fact]
    public async Task HistoryRequiresPatientAuthorization()
    {
        using var anonymous = _factory.CreateClient();
        using var administrator = await CreateAdministratorClientAsync();
        using var patient = await CreatePatientClientAsync("history-authorized@example.com", "79146825");

        await AssertErrorContractAsync(
            await anonymous.GetAsync("/api/appointments/patient/history?dni=79146825"),
            HttpStatusCode.Unauthorized,
            "AUTHENTICATION_FAILED",
            false);
        await AssertErrorContractAsync(
            await administrator.GetAsync("/api/appointments/patient/history?dni=79146825"),
            HttpStatusCode.Forbidden,
            "AUTHORIZATION_FAILED",
            false);
        Assert.Equal(
            HttpStatusCode.OK,
            (await patient.GetAsync("/api/appointments/patient/history?dni=79146825")).StatusCode);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("123456789")]
    [InlineData("1234A678")]
    public async Task HistoryRejectsInvalidDniWithValidationContract(string dni)
    {
        using var patient = await CreatePatientClientAsync("history-invalid-dni@example.com", "80257913");

        var response = await patient.GetAsync($"/api/appointments/patient/history?dni={dni}");

        await AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", true);
    }

    [Theory]
    [InlineData("١٢٣٤٥٦٧")]
    [InlineData("١٢٣٤٥٦٧٨")]
    public async Task HistoryRejectsNonAsciiUnicodeDigits(string dni)
    {
        using var patient = await CreatePatientClientAsync("history-unicode-dni@example.com", "80257913");

        var response = await patient.GetAsync(
            $"/api/appointments/patient/history?dni={Uri.EscapeDataString(dni)}");

        await AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", true);
    }

    [Fact]
    public async Task HistoryRequiresDniQueryParameter()
    {
        using var patient = await CreatePatientClientAsync("history-missing-dni@example.com", "80357912");

        var response = await patient.GetAsync("/api/appointments/patient/history");

        await AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task HistoryAcceptsPageSizeBoundaries(int pageSize)
    {
        using var patient = await CreatePatientClientAsync("history-page-size@example.com", "80457912");

        var response = await patient.GetAsync(
            $"/api/appointments/patient/history?dni=80457912&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<AppointmentModel.PagedResponse>())!;
        Assert.Equal(pageSize, history.PageSize);
    }

    [Fact]
    public async Task HistoryAcceptsMaximumPageIndexAndReturnsEmptyData()
    {
        using var patient = await CreatePatientClientAsync("history-max-page@example.com", "80557912");

        var response = await patient.GetAsync(
            "/api/appointments/patient/history?dni=80557912&pageIndex=1000000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = (await response.Content.ReadFromJsonAsync<AppointmentModel.PagedResponse>())!;
        Assert.Equal(1_000_000, history.PageIndex);
        Assert.Empty(history.Data);
    }

    [Theory]
    [InlineData("pageIndex=-1")]
    [InlineData("pageIndex=1000001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task HistoryRejectsInvalidPagination(string query)
    {
        using var patient = await CreatePatientClientAsync("history-invalid-page@example.com", "91368024");

        var response = await patient.GetAsync(
            $"/api/appointments/patient/history?dni=91368024&{query}");

        await AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", true);
    }

    [Fact]
    public async Task HistoryPaginatesAfterCountingAndUsesStableNewestFirstOrder()
    {
        using var patient = await CreatePatientClientAsync("history-pages@example.com", "12468035");
        var oldest = await AddAppointmentAsync(
            "12468035", AppointmentStatus.CANCELLED, new DateOnly(2026, 8, 7), new TimeOnly(9, 0));
        var tiedLowerId = await AddAppointmentAsync(
            "12468035", AppointmentStatus.ATTENDED, new DateOnly(2026, 8, 9), new TimeOnly(11, 0),
            id: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var tiedHigherId = await AddAppointmentAsync(
            "12468035", AppointmentStatus.NO_SHOW, new DateOnly(2026, 8, 9), new TimeOnly(11, 0),
            id: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        var first = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=12468035&pageIndex=0&pageSize=2"))!;
        var second = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=12468035&pageIndex=1&pageSize=2"))!;
        var outside = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=12468035&pageIndex=8&pageSize=2"))!;

        Assert.Equal(3, first.Total);
        Assert.Equal(3, second.Total);
        Assert.Equal([tiedHigherId.Id, tiedLowerId.Id], first.Data.Select(item => item.Id).ToArray());
        Assert.Equal(oldest.Id, Assert.Single(second.Data).Id);
        Assert.Empty(first.Data.Select(item => item.Id).Intersect(second.Data.Select(item => item.Id)));
        Assert.Equal(8, outside.PageIndex);
        Assert.Equal(2, outside.PageSize);
        Assert.Equal(3, outside.Total);
        Assert.Empty(outside.Data);
    }

    [Fact]
    public async Task RebookingKeepsCancellationInHistoryAndNewBookingActive()
    {
        var data = await CreateSlotAsync();
        using var patient = await CreatePatientClientAsync("history-rebooking@example.com", "23579146");
        var firstCreate = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "23579146"));
        var cancelled = (await firstCreate.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.Equal(HttpStatusCode.OK, (await patient.DeleteAsync($"/api/appointments/{cancelled.Id}")).StatusCode);
        var secondCreate = await patient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "23579146"));
        var rebooked = (await secondCreate.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;

        var history = (await patient.GetFromJsonAsync<AppointmentModel.PagedResponse>(
            "/api/appointments/patient/history?dni=23579146"))!;
        var active = (await patient.GetFromJsonAsync<List<AppointmentModel.Response>>(
            "/api/appointments/patient?dni=23579146"))!;

        Assert.Equal(cancelled.Id, Assert.Single(history.Data).Id);
        Assert.Equal("CANCELLED", history.Data.Single().Status);
        Assert.Equal(rebooked.Id, Assert.Single(active).Id);
        Assert.Equal("BOOKED", active.Single().Status);
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
    [InlineData(false, AppointmentStatus.BOOKED, "BOOKED", true)]
    [InlineData(true, AppointmentStatus.CANCELLED, "AVAILABLE", true)]
    public async Task AvailabilityUpdatePreservesSlotsReferencedByAppointments(
        bool cancel,
        AppointmentStatus expectedAppointmentStatus,
        string expectedSlotStatus,
        bool expectedSlotDeleted)
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        Assert.Equal(expectedAppointmentStatus, (await context.Appointments.FindAsync(appointment.Id))!.Status);
        var preservedSlot = (await context.AvailabilitySlots.FindAsync(data.Slot.Id))!;
        Assert.Equal(expectedSlotStatus, preservedSlot.Status);
        Assert.Equal(expectedSlotDeleted, preservedSlot.Deleted);
    }

    [Fact]
    public async Task AvailabilityUpdateReusesBookedSlotWhenReplacementKeepsItsSchedule()
    {
        var data = await CreateSlotAsync();
        using var owner = await CreatePatientClientAsync("same-schedule-owner@example.com", "92345678");
        var create = await owner.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "92345678"));
        var originalAppointment = (await create.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        using var administrator = await CreateAdministratorClientAsync();

        var update = await administrator.PutAsJsonAsync(
            "/api/availabilities",
            new AvailabilityRequestDto
            {
                DoctorId = data.Doctor.Id,
                Days = [new AvailabilityDayDto { Day = "MARTES", StartTime = "09:00", EndTime = "10:00" }]
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
            var matchingSlots = await context.AvailabilitySlots
                .Where(slot =>
                    slot.DoctorId == data.Doctor.Id &&
                    slot.SlotDate == data.Slot.SlotDate &&
                    slot.StartTime == data.Slot.StartTime)
                .ToListAsync();
            var preservedSlot = Assert.Single(matchingSlots);
            Assert.Equal(data.Slot.Id, preservedSlot.Id);
            Assert.Equal("BOOKED", preservedSlot.Status);
            Assert.False(preservedSlot.Deleted);
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await owner.DeleteAsync($"/api/appointments/{originalAppointment.Id}")).StatusCode);
        using var nextPatient = await CreatePatientClientAsync("same-schedule-next@example.com", "93456789");
        var rebook = await nextPatient.PostAsJsonAsync(
            "/api/appointments",
            Request(data.Doctor.Id, data.Slot.Id, "93456789"));

        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
        var replacementAppointment = (await rebook.Content.ReadFromJsonAsync<AppointmentModel.Response>())!;
        Assert.Equal(data.Slot.Id, replacementAppointment.AvailabilitySlotId);
        Assert.NotEqual(originalAppointment.Id, replacementAppointment.Id);
    }

    private async Task<Appointment> AddAppointmentAsync(
        string patientDni,
        AppointmentStatus status,
        DateOnly? date = null,
        TimeOnly? startTime = null,
        bool legacyDoctor = false,
        Guid? id = null)
    {
        var data = await CreateSlotAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Dsw2026TpiDbContext>();
        var slot = await context.AvailabilitySlots.FindAsync(data.Slot.Id);
        slot!.SlotDate = date ?? data.Slot.SlotDate;
        slot.StartTime = startTime ?? data.Slot.StartTime;
        slot.EndTime = slot.StartTime.AddMinutes(30);
        if (legacyDoctor)
        {
            await context.Set<Doctor>()
                .Where(doctor => doctor.Id == data.Doctor.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(doctor => doctor.SpecialityId, (Guid?)null));
        }

        var appointment = new Appointment
        {
            Id = id ?? Guid.NewGuid(),
            DoctorId = data.Doctor.Id,
            AvailabilitySlotId = data.Slot.Id,
            PatientDni = patientDni,
            Reason = "Consulta histórica",
            Status = status,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return appointment;
    }

    private static async Task AssertErrorContractAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode,
        bool expectDetails)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedErrorCode, body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
        var details = body.RootElement.GetProperty("details").EnumerateArray();
        if (expectDetails)
            Assert.NotEmpty(details);
        else
            Assert.Empty(details);
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
