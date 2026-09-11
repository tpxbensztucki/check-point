using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackRequests_ProjectMemberships_ProjectMembershipId",
                        column: x => x.ProjectMembershipId,
                        principalTable: "ProjectMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackRequests_ProjectMembershipId",
                table: "FeedbackRequests",
                column: "ProjectMembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackRequests");
        }
    }
}
