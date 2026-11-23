using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YetenekPlatformu.Migrations
{
    /// <inheritdoc />
    public partial class IbanWithCountryCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Iban",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<string>(
                name: "Iban",
                table: "AspNetUsers",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(7)",
                oldMaxLength: 7);

            // Mevcut kullanıcıların 7 haneli IBAN'ının başına "TR" ekle (yeni kural bunu şart koşuyor)
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [Iban] = N'TR' + [Iban] WHERE [Iban] NOT LIKE N'TR%'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Iban",
                table: "AspNetUsers",
                sql: "LEN([Iban]) = 9 AND [Iban] LIKE 'TR[0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_Iban",
                table: "AspNetUsers");

            // Geri alırken "TR"yi çıkar, 7 rakam kalsın
            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [Iban] = SUBSTRING([Iban], 3, 7) WHERE [Iban] LIKE N'TR%'");

            migrationBuilder.AlterColumn<string>(
                name: "Iban",
                table: "AspNetUsers",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(9)",
                oldMaxLength: 9);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_Iban",
                table: "AspNetUsers",
                sql: "LEN([Iban]) = 7 AND [Iban] NOT LIKE '%[^0-9]%'");
        }
    }
}
