using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackSubmissionAndLmNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoingWell = table.Column<string>(type: "text", nullable: false),
                    NotDoingWell = table.Column<string>(type: "text", nullable: false),
                    NeedsToImprove = table.Column<string>(type: "text", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackSubmissions_FeedbackRequests_FeedbackRequestId",
                        column: x => x.FeedbackRequestId,
                        principalTable: "FeedbackRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LmNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedbackSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineManagerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmNotifications_FeedbackSubmissions_FeedbackSubmissionId",
                        column: x => x.FeedbackSubmissionId,
                        principalTable: "FeedbackSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LmNotifications_People_LineManagerId",
                        column: x => x.LineManagerId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_FeedbackRequestId",
                table: "FeedbackSubmissions",
                column: "FeedbackRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LmNotifications_FeedbackSubmissionId",
                table: "LmNotifications",
                column: "FeedbackSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_LmNotifications_LineManagerId",
                table: "LmNotifications",
                column: "LineManagerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LmNotifications");

            migrationBuilder.DropTable(
                name: "FeedbackSubmissions");
        }
    }
}
