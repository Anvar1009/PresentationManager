using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PresentationManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndEvaluationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PresenterId",
                table: "Presentations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EvaluationCriteria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MaxScore = table.Column<int>(type: "integer", nullable: false),
                    OrderNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCriteria_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Judges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: true),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Judges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Judges_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PresentationId = table.Column<int>(type: "integer", nullable: false),
                    JudgeId = table.Column<int>(type: "integer", nullable: false),
                    CriterionId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scores_EvaluationCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "EvaluationCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scores_Judges_JudgeId",
                        column: x => x.JudgeId,
                        principalTable: "Judges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scores_Presentations_PresentationId",
                        column: x => x.PresentationId,
                        principalTable: "Presentations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Presentations_PresenterId",
                table: "Presentations",
                column: "PresenterId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCriteria_ProjectId",
                table: "EvaluationCriteria",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Judges_PhoneNumber",
                table: "Judges",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Judges_ProjectId",
                table: "Judges",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Judges_TelegramChatId",
                table: "Judges",
                column: "TelegramChatId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_CriterionId",
                table: "Scores",
                column: "CriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_JudgeId",
                table: "Scores",
                column: "JudgeId");

            migrationBuilder.CreateIndex(
                name: "IX_Scores_PresentationId_JudgeId_CriterionId",
                table: "Scores",
                columns: new[] { "PresentationId", "JudgeId", "CriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Presentations_Presenters_PresenterId",
                table: "Presentations",
                column: "PresenterId",
                principalTable: "Presenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Presentations_Presenters_PresenterId",
                table: "Presentations");

            migrationBuilder.DropTable(
                name: "Scores");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "EvaluationCriteria");

            migrationBuilder.DropTable(
                name: "Judges");

            migrationBuilder.DropIndex(
                name: "IX_Presentations_PresenterId",
                table: "Presentations");

            migrationBuilder.DropColumn(
                name: "PresenterId",
                table: "Presentations");
        }
    }
}
