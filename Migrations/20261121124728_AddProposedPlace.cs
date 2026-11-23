using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YetenekPlatformu.Migrations
{
    /// <inheritdoc />
    public partial class AddProposedPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProposedLatitude",
                table: "Appointments",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProposedLongitude",
                table: "Appointments",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProposedPlaceName",
                table: "Appointments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedLatitude",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ProposedLongitude",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ProposedPlaceName",
                table: "Appointments");
        }
    }
}
