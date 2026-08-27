using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase6_2Gestation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GestationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BreedingRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnimalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CurrentStage = table.Column<int>(type: "int", nullable: false),
                    HealthCheckNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GestationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GestationRecords_Animals_AnimalId",
                        column: x => x.AnimalId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GestationRecords_BreedingRecords_BreedingRecordId",
                        column: x => x.BreedingRecordId,
                        principalTable: "BreedingRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GestationRecords_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BirthRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GestationRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BreedingRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BirthDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OffspringCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    VetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BirthRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BirthRecords_Animals_DamId",
                        column: x => x.DamId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BirthRecords_BreedingRecords_BreedingRecordId",
                        column: x => x.BreedingRecordId,
                        principalTable: "BreedingRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BirthRecords_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BirthRecords_GestationRecords_GestationRecordId",
                        column: x => x.GestationRecordId,
                        principalTable: "GestationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "GestationHealthChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GestationRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GestationHealthChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GestationHealthChecks_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GestationHealthChecks_GestationRecords_GestationRecordId",
                        column: x => x.GestationRecordId,
                        principalTable: "GestationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BirthOffspring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BirthRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OffspringAnimalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TagNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SexOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    BirthWeightKg = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BirthOffspring", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BirthOffspring_Animals_OffspringAnimalId",
                        column: x => x.OffspringAnimalId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BirthOffspring_BirthRecords_BirthRecordId",
                        column: x => x.BirthRecordId,
                        principalTable: "BirthRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BirthOffspring_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BirthOffspring_SexOptions_SexOptionId",
                        column: x => x.SexOptionId,
                        principalTable: "SexOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BirthOffspring_BirthRecordId",
                table: "BirthOffspring",
                column: "BirthRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthOffspring_FarmId_TagNumber",
                table: "BirthOffspring",
                columns: new[] { "FarmId", "TagNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BirthOffspring_OffspringAnimalId",
                table: "BirthOffspring",
                column: "OffspringAnimalId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthOffspring_SexOptionId",
                table: "BirthOffspring",
                column: "SexOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_BreedingRecordId",
                table: "BirthRecords",
                column: "BreedingRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_DamId",
                table: "BirthRecords",
                column: "DamId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_FarmId_BirthDate",
                table: "BirthRecords",
                columns: new[] { "FarmId", "BirthDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_FarmId_DamId",
                table: "BirthRecords",
                columns: new[] { "FarmId", "DamId" });

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_GestationRecordId",
                table: "BirthRecords",
                column: "GestationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_GestationHealthChecks_FarmId_GestationRecordId",
                table: "GestationHealthChecks",
                columns: new[] { "FarmId", "GestationRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_GestationHealthChecks_GestationRecordId",
                table: "GestationHealthChecks",
                column: "GestationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_GestationRecords_AnimalId",
                table: "GestationRecords",
                column: "AnimalId");

            migrationBuilder.CreateIndex(
                name: "IX_GestationRecords_BreedingRecordId",
                table: "GestationRecords",
                column: "BreedingRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_GestationRecords_FarmId_AnimalId",
                table: "GestationRecords",
                columns: new[] { "FarmId", "AnimalId" });

            migrationBuilder.CreateIndex(
                name: "IX_GestationRecords_FarmId_BreedingRecordId",
                table: "GestationRecords",
                columns: new[] { "FarmId", "BreedingRecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BirthOffspring");

            migrationBuilder.DropTable(
                name: "GestationHealthChecks");

            migrationBuilder.DropTable(
                name: "BirthRecords");

            migrationBuilder.DropTable(
                name: "GestationRecords");
        }
    }
}
