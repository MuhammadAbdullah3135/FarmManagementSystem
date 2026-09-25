using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmTaskFarmIdAnimalIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FarmTasks_FarmId_AnimalId",
                table: "FarmTasks",
                columns: new[] { "FarmId", "AnimalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FarmTasks_FarmId_AnimalId",
                table: "FarmTasks");
        }
    }
}
