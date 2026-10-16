using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

public sealed class RoomMemberConfiguration
    : IEntityTypeConfiguration<RoomMember>
{
    public void Configure(
        EntityTypeBuilder<RoomMember> builder)
    {
        ConfigureTable(builder);
        ConfigureKey(builder);
        ConfigureProperties(builder);
    }

    private static void ConfigureTable(
        EntityTypeBuilder<RoomMember> builder)
    {
        builder.ToTable("room_members", table =>
        {
            table.HasCheckConstraint(
                "ck_room_members_display_name",
                "char_length(btrim(display_name)) BETWEEN 1 AND 40");
        });
    }

    private static void ConfigureKey(
        EntityTypeBuilder<RoomMember> builder)
    {
        builder.Property<Guid>("RoomId")
            .HasColumnName("room_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.HasKey(
                "RoomId",
                nameof(RoomMember.GuestId))
            .HasName("pk_room_members");
    }

    private static void ConfigureProperties(
        EntityTypeBuilder<RoomMember> builder)
    {
        builder.Property(member => member.GuestId)
            .HasColumnName("guest_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(member => member.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(member => member.JoinedAtUtc)
            .HasColumnName("joined_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}