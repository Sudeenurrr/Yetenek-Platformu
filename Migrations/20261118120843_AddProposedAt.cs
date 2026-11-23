using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YetenekPlatformu.Migrations
{
    /// <inheritdoc />
    public partial class AddProposedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProposedAt",
                table: "Appointments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedAt",
                table: "Appointments");
        }
    }
}
