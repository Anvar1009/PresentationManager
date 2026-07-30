using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PresentationManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastActiveProjectId",
                table: "Settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            // Existing installs may already have presentations with no project of their own — give them a
            // home instead of failing the NOT NULL/FK constraints added below. Projects is a brand-new table
            // at this point in the migration, so this insert is guaranteed to land as Id 1, which is what the
            // ProjectId column's defaultValue below relies on.
            migrationBuilder.Sql(
                "INSERT INTO Projects (Name, CreatedAt, UpdatedAt) VALUES ('Umumiy', datetime('now'), datetime('now'));");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Presentations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Presentations_ProjectId",
                table: "Presentations",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Presentations_Projects_ProjectId",
                table: "Presentations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Presentations_Projects_ProjectId",
                table: "Presentations");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Presentations_ProjectId",
                table: "Presentations");

            migrationBuilder.DropColumn(
                name: "LastActiveProjectId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Presentations");
        }
    }
}
