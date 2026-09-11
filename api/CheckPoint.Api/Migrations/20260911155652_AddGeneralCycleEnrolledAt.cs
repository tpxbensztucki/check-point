using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralCycleEnrolledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GeneralCycleEnrolledAt",
                table: "ProjectMemberships",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeneralCycleEnrolledAt",
                table: "ProjectMemberships");
        }
    }
}
