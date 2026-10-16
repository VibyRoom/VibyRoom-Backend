using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibyRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomMusicSessionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "room_music_sessions",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    max_pending_items = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_music_sessions", x => x.room_id);
                    table.CheckConstraint("ck_room_music_sessions_max_pending", "max_pending_items > 0");
                    table.CheckConstraint("ck_room_music_sessions_version", "version >= 0");
                    table.ForeignKey(
                        name: "fk_room_music_sessions_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "room_music_queue_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_by_guest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    artists = table.Column<string[]>(type: "text[]", nullable: false),
                    artwork_url = table.Column<string>(type: "text", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    external_url = table.Column<string>(type: "text", nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    track_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_music_queue_items", x => x.id);
                    table.CheckConstraint("ck_room_music_queue_items_duration", "duration_ms > 0");
                    table.CheckConstraint("ck_room_music_queue_items_position", "position >= 0");
                    table.CheckConstraint("ck_room_music_queue_items_title", "char_length(btrim(title)) BETWEEN 1 AND 300");
                    table.ForeignKey(
                        name: "fk_room_music_queue_items_sessions_room_id",
                        column: x => x.room_id,
                        principalTable: "room_music_sessions",
                        principalColumn: "room_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_room_music_queue_items_room_id",
                table: "room_music_queue_items",
                column: "room_id");
            
            migrationBuilder.Sql(
                """
                ALTER TABLE room_music_queue_items
                    ADD CONSTRAINT uq_room_music_queue_items_room_position
                    UNIQUE (room_id, position)
                    DEFERRABLE INITIALLY DEFERRED;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_music_queue_items");

            migrationBuilder.DropTable(
                name: "room_music_sessions");
        }
    }
}
