using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Tests.Unit.Domain;

public class DomainModelTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 10, 15, 0);

    [Fact]
    public void Doctor_ExposesAvailabilitiesAsReadOnlyCollection()
    {
        var doctor = NewDoctor();

        var rule = doctor.AddAvailability(2026, 8, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), Now);

        Assert.Same(rule, Assert.Single(doctor.Availabilities));
        Assert.Equal(doctor.Id, rule.DoctorId);
        Assert.False(doctor.Availabilities is ICollection<AvailabilityRule> { IsReadOnly: false });
    }

    [Fact]
    public void Rule_GeneratesThirtyMinuteSlotsForTheRestOfTheMonthSkippingPastAndNonWorkingDays()
    {
        var rule = NewDoctor().AddAvailability(2026, 8, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), Now);
        var holidays = new HashSet<DateOnly> { new(2026, 8, 17) };

        var created = rule.GenerateSlots(Now, holidays);

        // 10/08 ya pasó a las 10:15, 17/08 es feriado: quedan 24/08 y 31/08 con dos bloques cada uno.
        Assert.Equal(4, created.Count);
        Assert.Equal(created.Count, rule.Slots.Count);
        Assert.Equal([new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 31)], created.Select(slot => slot.SlotDate).Distinct());
        Assert.All(created, slot => Assert.Equal(SlotStatus.AVAILABLE, slot.Status));
        Assert.Empty(rule.GenerateSlots(Now, holidays));
    }

    [Theory]
    [InlineData(2026, 8, 12, 9, 0)]
    [InlineData(2026, 9, 7, 9, 0)]
    [InlineData(2026, 8, 17, 10, 0)]
    [InlineData(2026, 8, 17, 9, 15)]
    public void Rule_RejectsSlotsOutsideItsMonthDayOrRange(int year, int month, int day, int hour, int minute)
    {
        var rule = NewDoctor().AddAvailability(2026, 8, DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), Now);

        Assert.Throws<InvalidOperationException>(() =>
            rule.AddSlot(new DateOnly(year, month, day), new TimeOnly(hour, minute), Now));
        Assert.Empty(rule.Slots);
    }

    [Fact]
    public void Patient_BookingReservesTheSlotAndAddsTheAppointmentToBothSides()
    {
        var slot = NewSlot();
        var patient = new Patient("12345678", null, Now);

        var appointment = patient.Book(slot, "  Control anual  ", Now);

        Assert.Equal(AppointmentStatus.BOOKED, appointment.Status);
        Assert.Equal("Control anual", appointment.Reason);
        Assert.Equal(SlotStatus.BOOKED, slot.Status);
        Assert.Same(appointment, Assert.Single(patient.Appointments));
        Assert.Same(appointment, Assert.Single(slot.Appointments));
    }

    [Fact]
    public void Slot_AllowsOnlyOneActiveAppointment()
    {
        var slot = NewSlot();
        new Patient("12345678", null, Now).Book(slot, "Control anual", Now);
        var other = new Patient("87654321", null, Now);

        Assert.Throws<InvalidOperationException>(() => other.Book(slot, "Otra consulta", Now));
        Assert.Empty(other.Appointments);
        Assert.Single(slot.Appointments);
    }

    [Fact]
    public void Appointment_CancelRecordsDateAndReleasesTheSlotForANewBooking()
    {
        var slot = NewSlot();
        var appointment = new Patient("12345678", null, Now).Book(slot, "Control anual", Now);
        var cancelledAt = Now.AddHours(1);

        appointment.Cancel(cancelledAt);

        Assert.Equal(AppointmentStatus.CANCELLED, appointment.Status);
        Assert.Equal(cancelledAt, appointment.CancelledAt);
        Assert.Equal(SlotStatus.AVAILABLE, slot.Status);
        Assert.Throws<InvalidOperationException>(() => appointment.Cancel(cancelledAt));

        var rebooked = new Patient("87654321", null, Now).Book(slot, "Nueva consulta", Now);
        Assert.Equal(AppointmentStatus.BOOKED, rebooked.Status);
        Assert.Equal(2, slot.Appointments.Count);
    }

    [Fact]
    public void Appointment_AttendedRecordsDateAndClosesTheAppointment()
    {
        var appointment = new Patient("12345678", null, Now).Book(NewSlot(), "Control anual", Now);

        appointment.MarkAsAttended(Now);

        Assert.Equal(AppointmentStatus.ATTENDED, appointment.Status);
        Assert.Equal(Now, appointment.AttendedAt);
        Assert.Throws<InvalidOperationException>(() => appointment.MarkAsNoShow(Now));
    }

    [Fact]
    public void Patient_CannotBeRelinkedToAnotherUser()
    {
        var patient = new Patient("12345678", "user-1", Now);

        patient.LinkUser("user-1", Now);

        Assert.Throws<InvalidOperationException>(() => patient.LinkUser("user-2", Now));
        Assert.Equal("user-1", patient.UserId);
    }

    private static Doctor NewDoctor() =>
        new("Médico", "MP-1", new Speciality("Clínica", "Clínica médica general"));

    private static AvailabilitySlot NewSlot() =>
        NewDoctor()
            .AddAvailability(2026, 8, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(10, 0), Now)
            .AddSlot(new DateOnly(2026, 8, 11), new TimeOnly(9, 0), Now);
}
