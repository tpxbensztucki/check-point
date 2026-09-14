using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeCatchUpFeedbackRequestIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatchUps_FeedbackRequests_FeedbackRequestId",
                table: "CatchUps");

            migrationBuilder.AlterColumn<Guid>(
                name: "FeedbackRequestId",
                table: "CatchUps",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_CatchUps_FeedbackRequests_FeedbackRequestId",
                table: "CatchUps",
                column: "FeedbackRequestId",
                principalTable: "FeedbackRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatchUps_FeedbackRequests_FeedbackRequestId",
                table: "CatchUps");

            migrationBuilder.AlterColumn<Guid>(
                name: "FeedbackRequestId",
                table: "CatchUps",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CatchUps_FeedbackRequests_FeedbackRequestId",
                table: "CatchUps",
                column: "FeedbackRequestId",
                principalTable: "FeedbackRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
