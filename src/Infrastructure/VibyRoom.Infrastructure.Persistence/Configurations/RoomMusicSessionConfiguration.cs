using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Music.Playback;
using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

public sealed class RoomMusicSessionConfiguration
    : IEntityTypeConfiguration<RoomMusicSession>
{
    public void Configure(EntityTypeBuilder<RoomMusicSession> builder)
    {
        ConfigureTable(builder);
        ConfigureProperties(builder);
        ConfigureRoomRelationship(builder);
        ConfigureQueueItems(builder);
        ConfigurePlayback(builder);
    }

    private static void ConfigureTable(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.ToTable("room_music_sessions", table =>
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
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigurePlayback(
        EntityTypeBuilder<RoomMusicSession> builder)
    {
        builder.OwnsOne<PlaybackState>(
            session => session.Playback,
            playback =>
            {
                playback.ToTable("room_music_playback", table =>
                {
                    table.HasCheckConstraint(
                        "ck_room_music_playback_status",
                        "status IN ('stopped', 'playing', 'paused')");

                    table.HasCheckConstraint(
                        "ck_room_music_playback_position",
                        "position_anchor_ms >= 0");

                    table.HasCheckConstraint(
                        "ck_room_music_playback_consistency",
                        """
                        (status = 'stopped'
                            AND current_item IS NULL
                            AND position_anchor_ms = 0
                            AND anchor_at_utc IS NULL
                            AND next_transition_at_utc IS NULL)
                        OR
                        (status = 'paused'
                            AND current_item IS NOT NULL
                            AND anchor_at_utc IS NULL
                            AND next_transition_at_utc IS NULL)
                        OR
                        (status = 'playing'
                            AND current_item IS NOT NULL
                            AND anchor_at_utc IS NOT NULL
                            AND next_transition_at_utc IS NOT NULL)
                        """);
                });

                playback.WithOwner()
                    .HasForeignKey("RoomId")
                    .HasConstraintName(
                        "fk_room_music_playback_sessions_room_id");

                playback.Property<Guid>("RoomId")
                    .HasColumnName("room_id")
                    .HasColumnType("uuid")
                    .ValueGeneratedNever();

                playback.HasKey("RoomId")
                    .HasName("pk_room_music_playback");

                playback.Property<PlaybackStatus>(
                        state => state.Status)
                    .HasColumnName("status")
                    .HasColumnType("character varying(16)")
                    .HasMaxLength(16)
                    .HasConversion<string>(
                        value => value.ToString().ToLowerInvariant(),
                        value => Enum.Parse<PlaybackStatus>(
                            value, true))
                    .IsRequired();

                playback.Property<CurrentPlaybackItem?>(
                        state => state.CurrentItem)
                    .HasColumnName("current_item")
                    .HasColumnType("jsonb")
                    .HasConversion<string?>(
                        value =>
                            CurrentPlaybackItemJsonSerializer.Serialize(value),
                        value =>
                            CurrentPlaybackItemJsonSerializer.Deserialize(value))
                    .IsRequired(false);

                playback.Property<long>(
                        state => state.PositionAnchorMs)
                    .HasColumnName("position_anchor_ms")
                    .HasColumnType("bigint")
                    .IsRequired();

                playback.Property<DateTimeOffset?>(
                        state => state.AnchorAtUtc)
                    .HasColumnName("anchor_at_utc")
                    .HasColumnType("timestamp with time zone")
                    .IsRequired(false);

                playback.Property<DateTimeOffset?>(
                        "NextTransitionAtUtc")
                    .HasColumnName("next_transition_at_utc")
                    .HasColumnType("timestamp with time zone")
                    .IsRequired(false);

                playback.HasIndex("NextTransitionAtUtc")
                    .HasDatabaseName("ix_room_music_playback_due")
                    .HasFilter(
                        "status = 'playing' AND next_transition_at_utc IS NOT NULL");
            });

        builder.Navigation<PlaybackState>(
                session => session.Playback)
            .IsRequired();
    }
}