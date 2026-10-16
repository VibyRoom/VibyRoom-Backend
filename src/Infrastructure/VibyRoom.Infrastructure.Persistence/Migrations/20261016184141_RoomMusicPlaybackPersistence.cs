using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibyRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomMusicPlaybackPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "room_music_playback",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    current_item = table.Column<string>(type: "jsonb", nullable: true),
                    position_anchor_ms = table.Column<long>(type: "bigint", nullable: false),
                    anchor_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_transition_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_music_playback", x => x.room_id);
                    table.CheckConstraint("ck_room_music_playback_consistency", "(status = 'stopped'\n    AND current_item IS NULL\n    AND position_anchor_ms = 0\n    AND anchor_at_utc IS NULL\n    AND next_transition_at_utc IS NULL)\nOR\n(status = 'paused'\n    AND current_item IS NOT NULL\n    AND anchor_at_utc IS NULL\n    AND next_transition_at_utc IS NULL)\nOR\n(status = 'playing'\n    AND current_item IS NOT NULL\n    AND anchor_at_utc IS NOT NULL\n    AND next_transition_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_room_music_playback_position", "position_anchor_ms >= 0");
                    table.CheckConstraint("ck_room_music_playback_status", "status IN ('stopped', 'playing', 'paused')");
                    table.ForeignKey(
                        name: "fk_room_music_playback_sessions_room_id",
                        column: x => x.room_id,
                        principalTable: "room_music_sessions",
                        principalColumn: "room_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_room_music_playback_due",
                table: "room_music_playback",
                column: "next_transition_at_utc",
                filter: "status = 'playing' AND next_transition_at_utc IS NOT NULL");
            
            migrationBuilder.Sql(
                """
                INSERT INTO room_music_playback
                    (
                        room_id,
                        status,
                        current_item,
                        position_anchor_ms,
                        anchor_at_utc,
                        next_transition_at_utc
                    )
                SELECT
                    room_id,
                    'stopped',
                    NULL,
                    0,
                    NULL,
                    NULL
                FROM room_music_sessions
                ON CONFLICT (room_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_music_playback");
        }
    }
}
