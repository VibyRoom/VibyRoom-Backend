using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

public sealed class RoomMusicSessionConfiguration
    : IEntityTypeConfiguration<RoomMusicSession>
{
    public void Configure(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        ConfigureTable(builder);
        ConfigureProperties(builder);
        ConfigureRoomRelationship(builder);
        ConfigureQueueItems(builder);
    }

    private static void ConfigureTable(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.ToTable(
            "room_music_sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_room_music_sessions_version",
                    "version >= 0");

                table.HasCheckConstraint(
                    "ck_room_music_sessions_max_pending",
                    "max_pending_items > 0");
            });

        builder.HasKey(session => session.RoomId)
            .HasName("pk_room_music_sessions");
    }

    private static void ConfigureProperties(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.Property(session => session.RoomId)
            .HasColumnName("room_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(session => session.Version)
            .HasColumnName("version")
            .HasColumnType("bigint")
            .IsConcurrencyToken()
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(session => session.MaxPendingItems)
            .HasColumnName("max_pending_items")
            .HasColumnType("integer")
            .IsRequired();
    }

    private static void ConfigureRoomRelationship(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.HasOne<Room>()
            .WithOne()
            .HasForeignKey<RoomMusicSession>(
                session => session.RoomId)
            .HasConstraintName(
                "fk_room_music_sessions_rooms_room_id")
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureQueueItems(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.HasMany(session => session.PendingItems)
            .WithOne()
            .HasForeignKey("RoomId")
            .IsRequired()
            .HasConstraintName(
                "fk_room_music_queue_items_sessions_room_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(session => session.PendingItems)
            .HasField("_pendingItems")
            .UsePropertyAccessMode(
                PropertyAccessMode.Field);
    }
}