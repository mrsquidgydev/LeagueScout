using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeagueScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceEventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Game = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VenueName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Region = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    RegistrationUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GuildConfigurations",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    EventChannelId = table.Column<long>(type: "INTEGER", nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Regions = table.Column<string>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Radius = table.Column<double>(type: "REAL", nullable: true),
                    RadiusUnit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IncludeChallenges = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeCups = table.Column<bool>(type: "INTEGER", nullable: false),
                    LookAheadDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildConfigurations", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "EventRsvps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscordUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventRsvps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventRsvps_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuildEventMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildEventMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GuildEventMessages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventRsvps_EventId",
                table: "EventRsvps",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventRsvps_GuildId_DiscordUserId",
                table: "EventRsvps",
                columns: new[] { "GuildId", "DiscordUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_EventRsvps_GuildId_EventId_DiscordUserId",
                table: "EventRsvps",
                columns: new[] { "GuildId", "EventId", "DiscordUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_Source_SourceEventId",
                table: "Events",
                columns: new[] { "Source", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartDateTime",
                table: "Events",
                column: "StartDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_GuildEventMessages_EventId",
                table: "GuildEventMessages",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_GuildEventMessages_GuildId_EventId",
                table: "GuildEventMessages",
                columns: new[] { "GuildId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventRsvps");

            migrationBuilder.DropTable(
                name: "GuildConfigurations");

            migrationBuilder.DropTable(
                name: "GuildEventMessages");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
