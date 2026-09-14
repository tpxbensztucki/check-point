using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewStarterIntervalWeeks = table.Column<int[]>(type: "integer[]", nullable: false),
                    GeneralCycleSkipThresholdWeeks = table.Column<int>(type: "integer", nullable: false),
                    AutomaticRequestSendingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetTechPocCount = table.Column<int>(type: "integer", nullable: false),
                    TargetDmPocCount = table.Column<int>(type: "integer", nullable: false),
                    TargetOtherPocCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
