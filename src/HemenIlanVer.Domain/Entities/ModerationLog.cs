namespace HemenIlanVer.Domain.Entities;

public class ModerationLog : BaseEntity
{
    public Guid? ListingId { get; set; }
    public Listing? Listing { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
