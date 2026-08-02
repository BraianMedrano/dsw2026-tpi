using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dsw2026Tpi.Data.Migrations.Domain
{
    /// <inheritdoc />
    public partial class AddAppointmentsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "APPOINTMENTS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DoctorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AvailabilitySlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientDni = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_APPOINTMENTS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_APPOINTMENTS_AVAILABILITY_SLOTS_AvailabilitySlotId",
                        column: x => x.AvailabilitySlotId,
                        principalTable: "AVAILABILITY_SLOTS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_APPOINTMENTS_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_APPOINTMENTS_AvailabilitySlotId",
                table: "APPOINTMENTS",
                column: "AvailabilitySlotId");

            migrationBuilder.CreateIndex(
                name: "IX_APPOINTMENTS_DoctorId_AvailabilitySlotId",
                table: "APPOINTMENTS",
                columns: new[] { "DoctorId", "AvailabilitySlotId" });

            migrationBuilder.CreateIndex(
                name: "IX_APPOINTMENTS_PatientDni",
                table: "APPOINTMENTS",
                column: "PatientDni");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "APPOINTMENTS");
        }
    }
}
