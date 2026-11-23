using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedRecordMealSlotAndRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MealRole",
                table: "FeedRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealSlot",
                table: "FeedRecords",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MealRole",
                table: "FeedRecords");

            migrationBuilder.DropColumn(
                name: "MealSlot",
                table: "FeedRecords");
        }
    }
}
