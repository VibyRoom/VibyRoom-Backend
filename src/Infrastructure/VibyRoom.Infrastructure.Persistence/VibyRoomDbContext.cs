
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
namespace VibyRoom.Infrastructure.Persistence;

public sealed class VibyRoomDbContext : DbContext
{
    private const long InitialVersion = 1;

    public VibyRoomDbContext(
        DbContextOptions<VibyRoomDbContext> options)
        : base(options)
    {
    }

    public DbSet<Room> Rooms => Set<Room>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(VibyRoomDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareAggregateChanges();

        try
        {
            return await base.SaveChangesAsync(
                acceptAllChangesOnSuccess,
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new RoomConcurrencyException(
                "The room was modified by another operation. Reload the room and retry the operation.",
                exception);
        }
    }

    public override int SaveChanges(
        bool acceptAllChangesOnSuccess)
    {
        throw new NotSupportedException(
            "Synchronous saving is not supported. Use SaveChangesAsync.");
    }

    private void PrepareAggregateChanges()
    {
        ChangeTracker.DetectChanges();

        var roomsById = ChangeTracker
            .Entries<Room>()
            .ToDictionary(entry => entry.Entity.Id);

        var changedRoomIds = roomsById.Values
            .Where(entry =>
                entry.State is EntityState.Added
                    or EntityState.Modified)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        CollectMemberChanges(
            roomsById,
            changedRoomIds);

        var now = DateTimeOffset.UtcNow;

        foreach (var roomId in changedRoomIds)
        {
            var entry = roomsById[roomId];

            if (entry.State == EntityState.Added)
            {
                PrepareNewRoom(entry, now);
                continue;
            }

            if (entry.State is EntityState.Modified
                or EntityState.Unchanged)
            {
                PrepareExistingRoom(entry, now);
            }
        }
    }

    private void CollectMemberChanges(
        IReadOnlyDictionary<Guid, EntityEntry<Room>> roomsById,
        HashSet<Guid> changedRoomIds)
    {
        foreach (var memberEntry in ChangeTracker.Entries<RoomMember>())
        {
            if (memberEntry.State is not (
                    EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted))
            {
                continue;
            }

            var roomIdProperty =
                memberEntry.Property<Guid>("RoomId");

            var roomId = memberEntry.State == EntityState.Deleted
                ? roomIdProperty.OriginalValue
                : roomIdProperty.CurrentValue;

            if (!roomsById.TryGetValue(roomId, out var roomEntry))
            {
                throw new InvalidOperationException(
                    "RoomMember changes require a tracked Room aggregate.");
            }

            if (roomEntry.State == EntityState.Deleted)
            {
                continue;
            }

            changedRoomIds.Add(roomId);
        }
    }

    private static void PrepareNewRoom(
        EntityEntry<Room> entry,
        DateTimeOffset now)
    {
        entry.Property<long>("Version")
            .CurrentValue = InitialVersion;

        entry.Property<DateTimeOffset>("CreatedAtUtc")
            .CurrentValue = now;

        entry.Property<DateTimeOffset?>("ClosedAtUtc")
            .CurrentValue =
                entry.Entity.Status == RoomStatus.Closed
                    ? now
                    : null;
    }

    private static void PrepareExistingRoom(
        EntityEntry<Room> entry,
        DateTimeOffset now)
    {
        var versionProperty =
            entry.Property<long>("Version");

        versionProperty.CurrentValue = checked(
            versionProperty.OriginalValue + 1);

        versionProperty.IsModified = true;

        var statusProperty = entry.Property(
            room => room.Status);

        if (statusProperty.OriginalValue == RoomStatus.Active &&
            entry.Entity.Status == RoomStatus.Closed)
        {
            entry.Property<DateTimeOffset?>("ClosedAtUtc")
                .CurrentValue = now;
        }
    }
}