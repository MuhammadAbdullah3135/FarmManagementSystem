using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VetId",
                table: "VaccinationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VetId",
                table: "MedicalRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VetId",
                table: "BreedingRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VetId",
                table: "BirthRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vets_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_VetId",
                table: "VaccinationRecords",
                column: "VetId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_VetId",
                table: "MedicalRecords",
                column: "VetId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_VetId",
                table: "BreedingRecords",
                column: "VetId");

            migrationBuilder.CreateIndex(
                name: "IX_BirthRecords_VetId",
                table: "BirthRecords",
                column: "VetId");

            migrationBuilder.CreateIndex(
                name: "IX_Vets_FarmId_IsActive",
                table: "Vets",
                columns: new[] { "FarmId", "IsActive" });

            // Case-insensitive per-farm uniqueness for names, and for licence numbers only
            // when one is present. EF Core cannot model expression or partial indexes, so
            // these are the only indexes that enforce the rules; the service checks both
            // first so callers get a localised conflict instead of a database exception.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "IX_Vets_FarmId_NameLower"
                ON "Vets" ("FarmId", lower("Name"));

                CREATE UNIQUE INDEX "IX_Vets_FarmId_LicenseNumberLower"
                ON "Vets" ("FarmId", lower("LicenseNumber"))
                WHERE "LicenseNumber" IS NOT NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_BirthRecords_Vets_VetId",
                table: "BirthRecords",
                column: "VetId",
                principalTable: "Vets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BreedingRecords_Vets_VetId",
                table: "BreedingRecords",
                column: "VetId",
                principalTable: "Vets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalRecords_Vets_VetId",
                table: "MedicalRecords",
                column: "VetId",
                principalTable: "Vets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VaccinationRecords_Vets_VetId",
                table: "VaccinationRecords",
                column: "VetId",
                principalTable: "Vets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BirthRecords_Vets_VetId",
                table: "BirthRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_BreedingRecords_Vets_VetId",
                table: "BreedingRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_MedicalRecords_Vets_VetId",
                table: "MedicalRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VaccinationRecords_Vets_VetId",
                table: "VaccinationRecords");

            migrationBuilder.DropTable(
                name: "Vets");

            migrationBuilder.DropIndex(
                name: "IX_VaccinationRecords_VetId",
                table: "VaccinationRecords");

            migrationBuilder.DropIndex(
                name: "IX_MedicalRecords_VetId",
                table: "MedicalRecords");

            migrationBuilder.DropIndex(
                name: "IX_BreedingRecords_VetId",
                table: "BreedingRecords");

            migrationBuilder.DropIndex(
                name: "IX_BirthRecords_VetId",
                table: "BirthRecords");

            migrationBuilder.DropColumn(
                name: "VetId",
                table: "VaccinationRecords");

            migrationBuilder.DropColumn(
                name: "VetId",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "VetId",
                table: "BreedingRecords");

            migrationBuilder.DropColumn(
                name: "VetId",
                table: "BirthRecords");
        }
    }
}
