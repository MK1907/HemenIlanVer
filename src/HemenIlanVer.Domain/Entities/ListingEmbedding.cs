namespace HemenIlanVer.Domain.Entities;

public class ListingEmbedding : BaseEntity
{
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public string SearchableText { get; set; } = string.Empty;
}
