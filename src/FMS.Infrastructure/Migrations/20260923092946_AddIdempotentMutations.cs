using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotentMutations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientMutationId",
                table: "WeightRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletionClientMutationId",
                table: "FarmTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientMutationId",
                table: "AttendanceRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProcessedMutations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientMutationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedMutations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedMutations_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeightRecords_FarmId_ClientMutationId",
                table: "WeightRecords",
                columns: new[] { "FarmId", "ClientMutationId" },
                unique: true,
                filter: "\"ClientMutationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FarmTasks_FarmId_CompletionClientMutationId",
                table: "FarmTasks",
                columns: new[] { "FarmId", "CompletionClientMutationId" },
                unique: true,
                filter: "\"CompletionClientMutationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_FarmId_ClientMutationId",
                table: "AttendanceRecords",
                columns: new[] { "FarmId", "ClientMutationId" },
                unique: true,
                filter: "\"ClientMutationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMutations_FarmId_ClientMutationId",
                table: "ProcessedMutations",
                columns: new[] { "FarmId", "ClientMutationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMutations_FarmId_CreatedAt",
                table: "ProcessedMutations",
                columns: new[] { "FarmId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedMutations");

            migrationBuilder.DropIndex(
                name: "IX_WeightRecords_FarmId_ClientMutationId",
                table: "WeightRecords");

            migrationBuilder.DropIndex(
                name: "IX_FarmTasks_FarmId_CompletionClientMutationId",
                table: "FarmTasks");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceRecords_FarmId_ClientMutationId",
                table: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "ClientMutationId",
                table: "WeightRecords");

            migrationBuilder.DropColumn(
                name: "CompletionClientMutationId",
                table: "FarmTasks");

            migrationBuilder.DropColumn(
                name: "ClientMutationId",
                table: "AttendanceRecords");
        }
    }
}
