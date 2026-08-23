using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PresentationManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyPresenterUploadTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExistingPresentationId",
                table: "PresenterUploadTokens");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "PresenterUploadTokens");

            migrationBuilder.DropColumn(
                name: "ProjectName",
                table: "PresenterUploadTokens");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "PresenterUploadTokens");

            migrationBuilder.DropColumn(
                name: "UsedAt",
                table: "PresenterUploadTokens");

            migrationBuilder.AlterColumn<int>(
                name: "PresenterId",
                table: "PresenterUploadTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PresenterId",
                table: "PresenterUploadTokens",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ExistingPresentationId",
                table: "PresenterUploadTokens",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "PresenterUploadTokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProjectName",
                table: "PresenterUploadTokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "PresenterUploadTokens",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UsedAt",
                table: "PresenterUploadTokens",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
