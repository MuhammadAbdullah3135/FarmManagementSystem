using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToMedicalAndExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "MedicalRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "MedicalRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MedicalRecords",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Expenses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedBy",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Expenses");
        }
    }
}
