using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesLane.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGameOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGameOptions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Options = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGameOptions", x => new { x.UserId, x.GameType });
                    table.ForeignKey(
                        name: "FK_UserGameOptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserGameOptions");
        }
    }
}
