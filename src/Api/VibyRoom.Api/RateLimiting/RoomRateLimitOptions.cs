namespace VibyRoom.Api.RateLimiting;

public sealed class RoomRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public const string RoomCreationPolicy = "RoomCreation";

    public const string InvitationAccessPolicy = "InvitationAccess";

    public const string MusicCatalogPolicy = "MusicCatalog";

    public int RoomCreationPermitLimit { get; set; } = 5;

    public int RoomCreationWindowSeconds { get; set; } = 60;

    public int InvitationAccessPermitLimit { get; set; } = 30;

    public int InvitationAccessWindowSeconds { get; set; } = 60;

    public int MusicCatalogPermitLimit { get; set; } = 30;

    public int MusicCatalogWindowSeconds { get; set; } = 60;
}