namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomMemberEvent(
    Guid GuestId,
    string DisplayName,
    DateTimeOffset JoinedAtUtc)
{
    public const string MemberRole = "member";

    public string Role => MemberRole;
}