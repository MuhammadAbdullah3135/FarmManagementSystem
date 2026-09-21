using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFarmInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FarmInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FarmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FarmInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FarmInvitations_Farms_FarmId",
                        column: x => x.FarmId,
                        principalTable: "Farms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FarmInvitations_Email",
                table: "FarmInvitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_FarmInvitations_FarmId_Email",
                table: "FarmInvitations",
                columns: new[] { "FarmId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_FarmInvitations_TokenHash",
                table: "FarmInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_IsUsed",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "IsUsed" });

            // Farm-scoped roles move onto the shared app-role vocabulary so an
            // invitation can only assign role names the rest of the system knows.
            //
            // Values that actually occur in existing data: 'Owner' (written by
            // registration and farm creation) and 'FarmManager' (seeded).
            // 'Manager' is accepted by the old farm-update check but no code path
            // ever wrote it, so it is mapped defensively. 'Viewer' is already
            // canonical and left alone.
            migrationBuilder.Sql(
                "UPDATE \"UserFarms\" SET \"Role\" = 'SystemOwner' WHERE \"Role\" = 'Owner';");
            migrationBuilder.Sql(
                "UPDATE \"UserFarms\" SET \"Role\" = 'FarmManager' WHERE \"Role\" = 'Manager';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse only the deterministic 'Owner' -> 'SystemOwner' mapping.
            // 'FarmManager' is deliberately NOT reversed: it is the pre-existing
            // seeded value, so turning it back into 'Manager' would corrupt rows
            // this migration never changed.
            migrationBuilder.Sql(
                "UPDATE \"UserFarms\" SET \"Role\" = 'Owner' WHERE \"Role\" = 'SystemOwner';");

            migrationBuilder.DropTable(
                name: "FarmInvitations");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");
        }
    }
}
