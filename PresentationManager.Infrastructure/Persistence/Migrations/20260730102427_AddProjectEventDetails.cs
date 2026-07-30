using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PresentationManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectEventDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EventEndDate",
                table: "Projects",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EventStartDate",
                table: "Projects",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EventTime",
                table: "Projects",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Projects",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventEndDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EventStartDate",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "EventTime",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Projects");
        }
    }
}
