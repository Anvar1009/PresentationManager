using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PresentationManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Projects",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            // One-time backfill so introducing tenancy doesn't strand every row that existed before it -
            // every non-SuperAdmin user and every project gets folded into a single default tenant (2 is
            // UserRole.SuperAdmin's persisted int value, which deliberately stays organization-less - see
            // User.OrganizationId's own doc comment). Raw SQL rather than migrationBuilder.InsertData/UpdateData
            // because the backfill UPDATEs need the just-inserted row's generated Id, which InsertData can't
            // hand back within the same migration.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE default_org_id integer;
                BEGIN
                    INSERT INTO ""Organizations"" (""Name"", ""CreatedAt"")
                    VALUES ('Standart tashkilot', now()) RETURNING ""Id"" INTO default_org_id;

                    UPDATE ""Users"" SET ""OrganizationId"" = default_org_id WHERE ""Role"" <> 2;
                    UPDATE ""Projects"" SET ""OrganizationId"" = default_org_id;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganizationId",
                table: "Users",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OrganizationId",
                table: "Projects",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Organizations_OrganizationId",
                table: "Projects",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Organizations_OrganizationId",
                table: "Users",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Organizations_OrganizationId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Organizations_OrganizationId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Users_OrganizationId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Projects_OrganizationId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Projects");
        }
    }
}
