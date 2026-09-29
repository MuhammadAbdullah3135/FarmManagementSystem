using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPushSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                table: "NotificationPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Rows that predate the push channel arrive as false. They are a user's own
            // out-of-app choices from when email was the only one, so they are carried over
            // rather than reset: a type somebody silenced stays silenced on the new channel,
            // and one they wanted keeps reaching them. Both channels share one policy, which
            // is exactly what copying the column encodes. Without this, saving any preference
            // before the upgrade would silently mute push for that type.
            migrationBuilder.Sql(
                "UPDATE \"NotificationPreferences\" SET \"PushEnabled\" = \"EmailEnabled\";");

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId_DisabledAtUtc",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "DisabledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                table: "NotificationPreferences");
        }
    }
}
