using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bridge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelTopic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Topic",
                table: "channels",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Topic",
                table: "channels");
        }
    }
}
