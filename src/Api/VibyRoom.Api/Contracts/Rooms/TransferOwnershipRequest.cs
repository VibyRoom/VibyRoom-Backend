using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Rooms;

public sealed class TransferOwnershipRequest
    : IValidatableObject
{
    public Guid NewOwnerGuestId { get; init; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (NewOwnerGuestId == Guid.Empty)
        {
            yield return new ValidationResult(
                "NewOwnerGuestId must not be empty.",
                [nameof(NewOwnerGuestId)]);
        }
    }
}