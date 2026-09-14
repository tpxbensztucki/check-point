using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatchUpOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OutcomeNotes",
                table: "CatchUps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutcomeType",
                table: "CatchUps",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAt",
                table: "CatchUps",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OutcomeNotes",
                table: "CatchUps");

            migrationBuilder.DropColumn(
                name: "OutcomeType",
                table: "CatchUps");

            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "CatchUps");
        }
    }
}
