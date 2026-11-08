using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dsw2026Tpi.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddPatientDni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dni",
                table: "ApplicationUsers",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_Dni",
                table: "ApplicationUsers",
                column: "Dni",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ApplicationUsers_Dni",
                table: "ApplicationUsers");

            migrationBuilder.DropColumn(
                name: "Dni",
                table: "ApplicationUsers");
        }
    }
}
