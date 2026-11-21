using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesLane.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "DeviceTokens",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "DeviceTokens",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Ios");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Platform",
                table: "DeviceTokens");

            migrationBuilder.AlterColumn<string>(
                name: "Token",
                table: "DeviceTokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);
        }
    }
}
