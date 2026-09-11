using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CheckPoint.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeLead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PracticeLeadId",
                table: "Practices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Practices_PracticeLeadId",
                table: "Practices",
                column: "PracticeLeadId");

            migrationBuilder.AddForeignKey(
                name: "FK_Practices_People_PracticeLeadId",
                table: "Practices",
                column: "PracticeLeadId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Practices_People_PracticeLeadId",
                table: "Practices");

            migrationBuilder.DropIndex(
                name: "IX_Practices_PracticeLeadId",
                table: "Practices");

            migrationBuilder.DropColumn(
                name: "PracticeLeadId",
                table: "Practices");
        }
    }
}
