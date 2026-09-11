using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPoc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pocs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Relationship = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pocs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pocs_ProjectMemberships_ProjectMembershipId",
                        column: x => x.ProjectMembershipId,
                        principalTable: "ProjectMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pocs_ProjectMembershipId",
                table: "Pocs",
                column: "ProjectMembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pocs");
        }
    }
}
