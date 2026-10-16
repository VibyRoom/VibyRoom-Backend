using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

public sealed class QueueItemConfiguration
    : IEntityTypeConfiguration<QueueItem>
{
    public void Configure(
        EntityTypeBuilder<QueueItem> builder)
    {
        ConfigureTable(builder);
        ConfigureIdentity(builder);
        ConfigureMetadata(builder);
        ConfigureAudit(builder);
    }

    private static void ConfigureTable(
        EntityTypeBuilder<QueueItem> builder)
    {
        builder.ToTable(
            "room_music_queue_items",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_room_music_queue_items_position",
                    "position >= 0");

                table.HasCheckConstraint(
                    "ck_room_music_queue_items_duration",
                    "duration_ms > 0");

                table.HasCheckConstraint(
                    "ck_room_music_queue_items_title",
                    "char_length(btrim(title)) BETWEEN 1 AND 300");
            });

        builder.HasKey(item => item.Id)
            .HasName("pk_room_music_queue_items");

        // Track is a reconstructed immutable value object,
        // not an additional EF entity or navigation.
        builder.Ignore(item => item.Track);
    }

    private static void ConfigureIdentity(
        EntityTypeBuilder<QueueItem> builder)
    {
        builder.Property(item => item.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property<Guid>("RoomId")
            .HasColumnName("room_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.Position)
            .HasColumnName("position")
            .HasColumnType("integer")
            .IsRequired();
    }

    private static void ConfigureMetadata(
        EntityTypeBuilder<QueueItem> builder)
    {
        builder.Property<string>("_provider")
            .HasColumnName("provider")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property<string>("_trackId")
            .HasColumnName("track_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property<string>("_title")
            .HasColumnName("title")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property<string[]>("_artists")
            .HasColumnName("artists")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property<long>("_durationMs")
            .HasColumnName("duration_ms")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property<string?>("_artworkUrl")
            .HasColumnName("artwork_url")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property<string?>("_externalUrl")
            .HasColumnName("external_url")
            .HasColumnType("text")
            .IsRequired(false);
    }

    private static void ConfigureAudit(
        EntityTypeBuilder<QueueItem> builder)
    {
        builder.Property(item => item.AddedByGuestId)
            .HasColumnName("added_by_guest_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(item => item.AddedAtUtc)
            .HasColumnName("added_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}