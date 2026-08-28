using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWeightCheckSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_InventoryItems_InventoryItemId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationSchedules_VaccineTypes_VaccineTypeId1",
                table: "VaccinationSchedules");

            migrationBuilder.DropIndex(
                name: "IX_VaccinationSchedules_VaccineTypeId1",
                table: "VaccinationSchedules");

            migrationBuilder.DropColumn(
                name: "VaccineTypeId1",
                table: "VaccinationSchedules");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserEmail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeightCheckSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnimalTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BreedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgeCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecurrenceDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightCheckSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeightCheckSchedules_AgeCategories_AgeCategoryId",
                        column: x => x.AgeCategoryId,
                        principalTable: "AgeCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightCheckSchedules_AnimalTypes_AnimalTypeId",
                        column: x => x.AnimalTypeId,
                        principalTable: "AnimalTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightCheckSchedules_Breeds_BreedId",
                        column: x => x.BreedId,
                        principalTable: "Breeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WeightCheckSchedules_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_Timestamp",
                table: "AuditLogs",
                columns: new[] { "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_FarmId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "FarmId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "UserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_WeightCheckSchedules_AgeCategoryId",
                table: "WeightCheckSchedules",
                column: "AgeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightCheckSchedules_AnimalTypeId",
                table: "WeightCheckSchedules",
                column: "AnimalTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightCheckSchedules_BreedId",
                table: "WeightCheckSchedules",
                column: "BreedId");

            migrationBuilder.CreateIndex(
                name: "IX_WeightCheckSchedules_FarmId",
                table: "WeightCheckSchedules",
                column: "FarmId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_InventoryItems_InventoryItemId",
                table: "StockMovements",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_InventoryItems_InventoryItemId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "WeightCheckSchedules");

            migrationBuilder.AddColumn<Guid>(
                name: "VaccineTypeId1",
                table: "VaccinationSchedules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationSchedules_VaccineTypeId1",
                table: "VaccinationSchedules",
                column: "VaccineTypeId1");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_InventoryItems_InventoryItemId",
                table: "StockMovements",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationSchedules_VaccineTypes_VaccineTypeId1",
                table: "VaccinationSchedules",
                column: "VaccineTypeId1",
                principalTable: "VaccineTypes",
                principalColumn: "Id");
        }
    }
}
