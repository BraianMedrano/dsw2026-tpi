using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dsw2026Tpi.Data.Migrations.Domain
{
    /// <inheritdoc />
    public partial class AlignDoctorSoftDeleteContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "Doctors",
                newName: "Deleted");

            migrationBuilder.Sql(
                """UPDATE "Doctors" SET "Deleted" = CASE "Deleted" WHEN 1 THEN 0 ELSE 1 END;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Deleted",
                table: "Doctors",
                newName: "IsActive");

            migrationBuilder.Sql(
                """UPDATE "Doctors" SET "IsActive" = CASE "IsActive" WHEN 1 THEN 0 ELSE 1 END;""");
        }
    }
}
