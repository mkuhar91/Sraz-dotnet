using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sraz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoomLeaderboardResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GameId",
                table: "LeaderboardEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "LeaderboardEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Won",
                table: "LeaderboardEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_GameId_PlayerId",
                table: "LeaderboardEntries",
                columns: new[] { "GameId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardEntries_PlayerId",
                table: "LeaderboardEntries",
                column: "PlayerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeaderboardEntries_GameId_PlayerId",
                table: "LeaderboardEntries");

            migrationBuilder.DropIndex(
                name: "IX_LeaderboardEntries_PlayerId",
                table: "LeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "LeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "LeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "Won",
                table: "LeaderboardEntries");
        }
    }
}
