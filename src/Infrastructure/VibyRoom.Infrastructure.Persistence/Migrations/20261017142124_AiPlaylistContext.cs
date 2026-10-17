using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibyRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiPlaylistContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_history",
                table: "room_music_sessions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "ai_prompt",
                table: "room_music_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_history",
                table: "room_music_sessions");

            migrationBuilder.DropColumn(
                name: "ai_prompt",
                table: "room_music_sessions");
        }
    }
}
