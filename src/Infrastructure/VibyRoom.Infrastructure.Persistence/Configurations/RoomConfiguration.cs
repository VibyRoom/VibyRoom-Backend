using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        ConfigureTable(builder);
        ConfigureProperties(builder);
        ConfigureIndexes(builder);
        ConfigureShadowProperties(builder);
        ConfigureMembers(builder);
    }

    private static void ConfigureTable(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms", table =>
        {
            table.HasCheckConstraint(
                "ck_rooms_name",
                "char_length(btrim(name)) BETWEEN 1 AND 100");

            table.HasCheckConstraint(
                "ck_rooms_invite_code",
                "invite_code ~ '^[A-Za-z0-9_-]{22}$'");

            table.HasCheckConstraint(
                "ck_rooms_capacity",
                "capacity > 0");

            table.HasCheckConstraint(
                "ck_rooms_status",
                "status IN ('Active', 'Closed')");

            table.HasCheckConstraint(
                "ck_rooms_owner",
                "(status = 'Active' AND owner_guest_id IS NOT NULL) " +
                "OR (status = 'Closed' AND owner_guest_id IS NULL)");

            table.HasCheckConstraint(
                "ck_rooms_closed_at",
                "(status = 'Active' AND closed_at_utc IS NULL) " +
                "OR (status = 'Closed' AND closed_at_utc IS NOT NULL)");
        });

        builder.HasKey(room => room.Id)
            .HasName("pk_rooms");
    }

    private static void ConfigureProperties(EntityTypeBuilder<Room> builder)
    {
        builder.Property(room => room.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(room => room.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(room => room.InviteCode)
            .HasColumnName("invite_code")
            .HasMaxLength(22)
            .UseCollation("C")
            .IsRequired();

        builder.Property(room => room.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(room => room.OwnerGuestId)
            .HasColumnName("owner_guest_id")
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(room => room.Capacity)
            .HasColumnName("capacity")
            .IsRequired();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<Room> builder)
    {
        builder.HasIndex(room => room.InviteCode)
            .IsUnique()
            .HasDatabaseName("ux_rooms_invite_code");
    }

    private static void ConfigureShadowProperties(
        EntityTypeBuilder<Room> builder)
    {
        builder.Property<long>("Version")
            .HasColumnName("version")
            .HasColumnType("bigint")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property<DateTimeOffset>("CreatedAtUtc")
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property<DateTimeOffset?>("ClosedAtUtc")
            .HasColumnName("closed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
    }

    private static void ConfigureMembers(
        EntityTypeBuilder<Room> builder)
    {
        builder.HasMany(room => room.Members)
            .WithOne()
            .HasForeignKey("RoomId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(room => room.Members)
            .HasField("_members")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}