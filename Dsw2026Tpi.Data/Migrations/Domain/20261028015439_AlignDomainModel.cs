using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dsw2026Tpi.Data.Migrations.Domain
{
    /// <summary>
    /// Alinea el esquema con el modelo de datos del TPI: tabla PATIENTS, APPOINTMENTS.PatientId en lugar del DNI
    /// suelto, fechas de cancelación y atención, especialidad obligatoria y día de semana numérico.
    /// Los pasos de datos se ejecutan antes que los cambios que SQLite resuelve reconstruyendo tablas.
    /// </summary>
    public partial class AlignDomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Médicos sin especialidad: eran datos de prueba heredados y el modelo exige la especialidad.
            migrationBuilder.Sql("""
                DELETE FROM APPOINTMENTS
                WHERE DoctorId IN (SELECT Id FROM Doctors WHERE SpecialityId IS NULL)
                   OR AvailabilitySlotId IN (
                        SELECT slot.Id
                        FROM AVAILABILITY_SLOTS slot
                        JOIN AVAILABILITY_RULES rule ON rule.Id = slot.AvailabilityRuleId
                        JOIN Doctors doctor ON doctor.Id = rule.DoctorId
                        WHERE doctor.SpecialityId IS NULL);
                DELETE FROM AVAILABILITY_SLOTS
                WHERE AvailabilityRuleId IN (
                    SELECT rule.Id
                    FROM AVAILABILITY_RULES rule
                    JOIN Doctors doctor ON doctor.Id = rule.DoctorId
                    WHERE doctor.SpecialityId IS NULL);
                DELETE FROM AVAILABILITY_RULES
                WHERE DoctorId IN (SELECT Id FROM Doctors WHERE SpecialityId IS NULL);
                DELETE FROM Doctors WHERE SpecialityId IS NULL;
                """);

            // Reglas y turnos pasan a EntityBase, cuyo UpdatedAt es obligatorio.
            migrationBuilder.Sql("UPDATE AVAILABILITY_RULES SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;");
            migrationBuilder.Sql("UPDATE AVAILABILITY_SLOTS SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;");

            // day_of_week se guarda como el valor numérico de System.DayOfWeek.
            migrationBuilder.Sql("""
                UPDATE AVAILABILITY_RULES SET DayOfWeek = CASE DayOfWeek
                    WHEN 'Sunday' THEN '0' WHEN 'Monday' THEN '1' WHEN 'Tuesday' THEN '2'
                    WHEN 'Wednesday' THEN '3' WHEN 'Thursday' THEN '4' WHEN 'Friday' THEN '5'
                    WHEN 'Saturday' THEN '6' ELSE DayOfWeek END;
                """);

            migrationBuilder.CreateTable(
                name: "PATIENTS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dni = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PATIENTS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PATIENTS_Dni",
                table: "PATIENTS",
                column: "Dni",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PATIENTS_UserId",
                table: "PATIENTS",
                column: "UserId",
                unique: true);

            // Un paciente por cada DNI que ya tenía citas. El usuario de Identity vive en otro contexto:
            // UserId queda vacío y se asocia en el próximo login del paciente.
            migrationBuilder.Sql("""
                INSERT INTO PATIENTS (Id, Dni, Deleted, CreatedAt, UpdatedAt)
                SELECT upper(substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-' || substr(h, 13, 4) || '-' ||
                             substr(h, 17, 4) || '-' || substr(h, 21, 12)),
                       Dni, 0, FirstSeen, FirstSeen
                FROM (SELECT PatientDni AS Dni, hex(randomblob(16)) AS h, MIN(CreatedAt) AS FirstSeen
                      FROM APPOINTMENTS
                      GROUP BY PatientDni);
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "APPOINTMENTS",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendedAt",
                table: "APPOINTMENTS",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "APPOINTMENTS",
                type: "TEXT",
                nullable: true);

            // Antes de este cambio la única marca temporal de la transición era UpdatedAt.
            migrationBuilder.Sql("""
                UPDATE APPOINTMENTS SET
                    PatientId = (SELECT patient.Id FROM PATIENTS patient WHERE patient.Dni = APPOINTMENTS.PatientDni),
                    CancelledAt = CASE WHEN Status = 'CANCELLED' THEN UpdatedAt END,
                    AttendedAt = CASE WHEN Status = 'ATTENDED' THEN UpdatedAt END;
                """);

            // A partir de aquí SQLite reconstruye tablas; los datos ya están migrados.
            migrationBuilder.DropForeignKey(
                name: "FK_APPOINTMENTS_Doctors_DoctorId",
                table: "APPOINTMENTS");

            migrationBuilder.DropIndex(
                name: "IX_APPOINTMENTS_AvailabilitySlotId",
                table: "APPOINTMENTS");

            migrationBuilder.DropIndex(
                name: "IX_APPOINTMENTS_DoctorId_AvailabilitySlotId",
                table: "APPOINTMENTS");

            migrationBuilder.DropIndex(
                name: "IX_APPOINTMENTS_PatientDni",
                table: "APPOINTMENTS");

            migrationBuilder.DropColumn(
                name: "PatientDni",
                table: "APPOINTMENTS");

            // El médico de la cita se obtiene del turno (APPOINTMENTS no tiene doctor_id en el modelo de datos).
            migrationBuilder.DropColumn(
                name: "DoctorId",
                table: "APPOINTMENTS");

            migrationBuilder.AlterColumn<Guid>(
                name: "SpecialityId",
                table: "Doctors",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "AVAILABILITY_SLOTS",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AVAILABILITY_SLOTS",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20,
                oldDefaultValue: "AVAILABLE");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "AVAILABILITY_RULES",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DayOfWeek",
                table: "AVAILABILITY_RULES",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "IX_APPOINTMENTS_AvailabilitySlotId",
                table: "APPOINTMENTS",
                column: "AvailabilitySlotId",
                unique: true,
                filter: "\"Status\" = 'BOOKED'");

            migrationBuilder.CreateIndex(
                name: "IX_APPOINTMENTS_PatientId",
                table: "APPOINTMENTS",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_APPOINTMENTS_PATIENTS_PatientId",
                table: "APPOINTMENTS",
                column: "PatientId",
                principalTable: "PATIENTS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite difiere las reconstrucciones de EF al final del script, después de un DropTable("PATIENTS")
            // que fallaría por la FK. APPOINTMENTS se reconstruye explícitamente para recuperar DoctorId y
            // PatientDni desde el turno y el paciente antes de eliminar PATIENTS.
            migrationBuilder.Sql("""
                CREATE TABLE "ef_temp_APPOINTMENTS" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_APPOINTMENTS" PRIMARY KEY,
                    "AvailabilitySlotId" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "DoctorId" TEXT NOT NULL,
                    "PatientDni" TEXT NOT NULL,
                    "Reason" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "UpdatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_APPOINTMENTS_AVAILABILITY_SLOTS_AvailabilitySlotId" FOREIGN KEY ("AvailabilitySlotId") REFERENCES "AVAILABILITY_SLOTS" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_APPOINTMENTS_Doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES "Doctors" ("Id") ON DELETE RESTRICT
                );
                INSERT INTO "ef_temp_APPOINTMENTS" ("Id", "AvailabilitySlotId", "CreatedAt", "DoctorId", "PatientDni", "Reason", "Status", "UpdatedAt")
                SELECT appointment.Id, appointment.AvailabilitySlotId, appointment.CreatedAt, slot.DoctorId,
                       patient.Dni, appointment.Reason, appointment.Status, appointment.UpdatedAt
                FROM APPOINTMENTS appointment
                JOIN AVAILABILITY_SLOTS slot ON slot.Id = appointment.AvailabilitySlotId
                JOIN PATIENTS patient ON patient.Id = appointment.PatientId;
                DROP TABLE "APPOINTMENTS";
                ALTER TABLE "ef_temp_APPOINTMENTS" RENAME TO "APPOINTMENTS";
                CREATE INDEX "IX_APPOINTMENTS_AvailabilitySlotId" ON "APPOINTMENTS" ("AvailabilitySlotId");
                CREATE INDEX "IX_APPOINTMENTS_DoctorId_AvailabilitySlotId" ON "APPOINTMENTS" ("DoctorId", "AvailabilitySlotId");
                CREATE INDEX "IX_APPOINTMENTS_PatientDni" ON "APPOINTMENTS" ("PatientDni");
                """);

            migrationBuilder.DropTable(
                name: "PATIENTS");

            migrationBuilder.Sql("""
                UPDATE AVAILABILITY_RULES SET DayOfWeek = CASE DayOfWeek
                    WHEN 0 THEN 'Sunday' WHEN 1 THEN 'Monday' WHEN 2 THEN 'Tuesday'
                    WHEN 3 THEN 'Wednesday' WHEN 4 THEN 'Thursday' WHEN 5 THEN 'Friday'
                    WHEN 6 THEN 'Saturday' ELSE DayOfWeek END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SpecialityId",
                table: "Doctors",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "AVAILABILITY_SLOTS",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AVAILABILITY_SLOTS",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "AVAILABLE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "UpdatedAt",
                table: "AVAILABILITY_RULES",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "DayOfWeek",
                table: "AVAILABILITY_RULES",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
