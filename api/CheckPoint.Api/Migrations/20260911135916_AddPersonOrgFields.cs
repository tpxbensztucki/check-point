using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonOrgFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HeadOfPracticeId",
                table: "People",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LineManagerId",
                table: "People",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PracticeId",
                table: "People",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "People",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_People_HeadOfPracticeId",
                table: "People",
                column: "HeadOfPracticeId");

            migrationBuilder.CreateIndex(
                name: "IX_People_LineManagerId",
                table: "People",
                column: "LineManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_People_PracticeId",
                table: "People",
                column: "PracticeId");

            migrationBuilder.AddForeignKey(
                name: "FK_People_People_HeadOfPracticeId",
                table: "People",
                column: "HeadOfPracticeId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_People_LineManagerId",
                table: "People",
                column: "LineManagerId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Practices_PracticeId",
                table: "People",
                column: "PracticeId",
                principalTable: "Practices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_People_People_HeadOfPracticeId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_People_LineManagerId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Practices_PracticeId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_HeadOfPracticeId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_LineManagerId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_People_PracticeId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "HeadOfPracticeId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "LineManagerId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "PracticeId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "People");
        }
    }
}
