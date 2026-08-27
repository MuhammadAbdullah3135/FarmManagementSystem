using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase6Breeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AverageGestationDays",
                table: "Breeds",
                type: "int",
                nullable: false,
                defaultValue: 283);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemDefined",
                table: "AnimalStatuses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "DamId",
                table: "Animals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SireId",
                table: "Animals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BreedingRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FarmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SireId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BreedingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    VetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BreedingRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BreedingRecords_Animals_DamId",
                        column: x => x.DamId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BreedingRecords_Animals_SireId",
                        column: x => x.SireId,
                        principalTable: "Animals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BreedingRecords_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Animals_DamId",
                table: "Animals",
                column: "DamId");

            migrationBuilder.CreateIndex(
                name: "IX_Animals_SireId",
                table: "Animals",
                column: "SireId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_DamId",
                table: "BreedingRecords",
                column: "DamId");

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_FarmId_BreedingDate",
                table: "BreedingRecords",
                columns: new[] { "FarmId", "BreedingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_FarmId_DamId",
                table: "BreedingRecords",
                columns: new[] { "FarmId", "DamId" });

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_FarmId_SireId",
                table: "BreedingRecords",
                columns: new[] { "FarmId", "SireId" });

            migrationBuilder.CreateIndex(
                name: "IX_BreedingRecords_SireId",
                table: "BreedingRecords",
                column: "SireId");

            migrationBuilder.AddForeignKey(
                name: "FK_Animals_Animals_DamId",
                table: "Animals",
                column: "DamId",
                principalTable: "Animals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Animals_Animals_SireId",
                table: "Animals",
                column: "SireId",
                principalTable: "Animals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Animals_Animals_DamId",
                table: "Animals");

            migrationBuilder.DropForeignKey(
                name: "FK_Animals_Animals_SireId",
                table: "Animals");

            migrationBuilder.DropTable(
                name: "BreedingRecords");

            migrationBuilder.DropIndex(
                name: "IX_Animals_DamId",
                table: "Animals");

            migrationBuilder.DropIndex(
                name: "IX_Animals_SireId",
                table: "Animals");

            migrationBuilder.DropColumn(
                name: "AverageGestationDays",
                table: "Breeds");

            migrationBuilder.DropColumn(
                name: "IsSystemDefined",
                table: "AnimalStatuses");

            migrationBuilder.DropColumn(
                name: "DamId",
                table: "Animals");

            migrationBuilder.DropColumn(
                name: "SireId",
                table: "Animals");
        }
    }
}
