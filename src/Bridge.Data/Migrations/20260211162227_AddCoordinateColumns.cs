using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bridge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoordinateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildingX",
                table: "channels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BuildingZ",
                table: "channels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VillageIndex",
                table: "channel_groups",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VillageX",
                table: "channel_groups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VillageZ",
                table: "channel_groups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_groups_CenterX_CenterZ",
                table: "channel_groups",
                columns: new[] { "CenterX", "CenterZ" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channel_groups_CenterX_CenterZ",
                table: "channel_groups");

            migrationBuilder.DropColumn(
                name: "BuildingX",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "BuildingZ",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "VillageIndex",
                table: "channel_groups");

            migrationBuilder.DropColumn(
                name: "VillageX",
                table: "channel_groups");

            migrationBuilder.DropColumn(
                name: "VillageZ",
                table: "channel_groups");
        }
    }
}
