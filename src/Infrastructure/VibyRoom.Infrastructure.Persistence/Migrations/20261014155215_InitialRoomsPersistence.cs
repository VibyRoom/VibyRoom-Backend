using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibyRoom.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRoomsPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    invite_code = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false, collation: "C"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_guest_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                    table.CheckConstraint("ck_rooms_capacity", "capacity > 0");
                    table.CheckConstraint("ck_rooms_closed_at", "(status = 'Active' AND closed_at_utc IS NULL) OR (status = 'Closed' AND closed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_rooms_invite_code", "invite_code ~ '^[A-Za-z0-9_-]{22}$'");
                    table.CheckConstraint("ck_rooms_name", "char_length(btrim(name)) BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_rooms_owner", "(status = 'Active' AND owner_guest_id IS NOT NULL) OR (status = 'Closed' AND owner_guest_id IS NULL)");
                    table.CheckConstraint("ck_rooms_status", "status IN ('Active', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "room_members",
                columns: table => new
                {
                    guest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    joined_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_members", x => new { x.room_id, x.guest_id });
                    table.CheckConstraint("ck_room_members_display_name", "char_length(btrim(display_name)) BETWEEN 1 AND 40");
                    table.ForeignKey(
                        name: "FK_room_members_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_rooms_invite_code",
                table: "rooms",
                column: "invite_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_members");

            migrationBuilder.DropTable(
                name: "rooms");
        }
    }
}
