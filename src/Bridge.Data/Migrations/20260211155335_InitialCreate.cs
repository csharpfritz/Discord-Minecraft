using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Bridge.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DiscordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CenterX = table.Column<int>(type: "integer", nullable: false),
                    CenterZ = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "generation_jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiscordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinecraftUuid = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    MinecraftUsername = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LastLocationX = table.Column<int>(type: "integer", nullable: true),
                    LastLocationY = table.Column<int>(type: "integer", nullable: true),
                    LastLocationZ = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "world_state",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VillageCenterX = table.Column<int>(type: "integer", nullable: false),
                    VillageCenterZ = table.Column<int>(type: "integer", nullable: false),
                    BuildingWidth = table.Column<int>(type: "integer", nullable: false),
                    BuildingDepth = table.Column<int>(type: "integer", nullable: false),
                    BuildingHeight = table.Column<int>(type: "integer", nullable: false),
                    TrackStartX = table.Column<int>(type: "integer", nullable: true),
                    TrackStartZ = table.Column<int>(type: "integer", nullable: true),
                    TrackEndX = table.Column<int>(type: "integer", nullable: true),
                    TrackEndZ = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_world_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DiscordId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelGroupId = table.Column<int>(type: "integer", nullable: false),
                    BuildingIndex = table.Column<int>(type: "integer", nullable: false),
                    CoordinateX = table.Column<int>(type: "integer", nullable: false),
                    CoordinateZ = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_channels_channel_groups_ChannelGroupId",
                        column: x => x.ChannelGroupId,
                        principalTable: "channel_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_groups_DiscordId",
                table: "channel_groups",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_ChannelGroupId",
                table: "channels",
                column: "ChannelGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_channels_DiscordId",
                table: "channels",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_generation_jobs_Status",
                table: "generation_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_players_DiscordId",
                table: "players",
                column: "DiscordId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_players_MinecraftUuid",
                table: "players",
                column: "MinecraftUuid",
                unique: true,
                filter: "\"MinecraftUuid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_world_state_Key",
                table: "world_state",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "generation_jobs");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "world_state");

            migrationBuilder.DropTable(
                name: "channel_groups");
        }
    }
}
