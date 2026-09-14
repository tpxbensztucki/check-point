using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPocReferenceToMagicLinkAndSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedbackSubmissions_FeedbackRequestId",
                table: "FeedbackSubmissions");

            migrationBuilder.AddColumn<Guid>(
                name: "PocId",
                table: "MagicLinks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "PocId",
                table: "FeedbackSubmissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_MagicLinks_PocId",
                table: "MagicLinks",
                column: "PocId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_FeedbackRequestId_PocId",
                table: "FeedbackSubmissions",
                columns: new[] { "FeedbackRequestId", "PocId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_PocId",
                table: "FeedbackSubmissions",
                column: "PocId");

            migrationBuilder.AddForeignKey(
                name: "FK_FeedbackSubmissions_Pocs_PocId",
                table: "FeedbackSubmissions",
                column: "PocId",
                principalTable: "Pocs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MagicLinks_Pocs_PocId",
                table: "MagicLinks",
                column: "PocId",
                principalTable: "Pocs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FeedbackSubmissions_Pocs_PocId",
                table: "FeedbackSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_MagicLinks_Pocs_PocId",
                table: "MagicLinks");

            migrationBuilder.DropIndex(
                name: "IX_MagicLinks_PocId",
                table: "MagicLinks");

            migrationBuilder.DropIndex(
                name: "IX_FeedbackSubmissions_FeedbackRequestId_PocId",
                table: "FeedbackSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_FeedbackSubmissions_PocId",
                table: "FeedbackSubmissions");

            migrationBuilder.DropColumn(
                name: "PocId",
                table: "MagicLinks");

            migrationBuilder.DropColumn(
                name: "PocId",
                table: "FeedbackSubmissions");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackSubmissions_FeedbackRequestId",
                table: "FeedbackSubmissions",
                column: "FeedbackRequestId",
                unique: true);
        }
    }
}
