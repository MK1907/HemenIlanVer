namespace HemenIlanVer.Domain.Entities;

public class Conversation : BaseEntity
{
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public Guid BuyerUserId { get; set; }
    public Guid SellerUserId { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
