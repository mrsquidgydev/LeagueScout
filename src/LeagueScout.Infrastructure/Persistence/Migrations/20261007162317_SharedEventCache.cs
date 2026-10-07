using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeagueScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SharedEventCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "GuildConfigurations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissingCount",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DatasetSyncStates",
                columns: table => new
                {
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatasetKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastFailureStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAllowedRequestAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastResultCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetSyncStates", x => new { x.Source, x.DatasetKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Country_StartDateTime",
                table: "Events",
                columns: new[] { "Country", "StartDateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetSyncStates");

            migrationBuilder.DropIndex(
                name: "IX_Events_Country_StartDateTime",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "GuildConfigurations");

            migrationBuilder.DropColumn(
                name: "MissingCount",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "Events");
        }
    }
}
