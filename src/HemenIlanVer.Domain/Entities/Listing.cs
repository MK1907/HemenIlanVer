using HemenIlanVer.Domain.Enums;

namespace HemenIlanVer.Domain.Entities;

public class Listing : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "TRY";
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public ListingType ListingType { get; set; }
    public Guid? CityId { get; set; }
    public City? City { get; set; }
    public Guid? DistrictId { get; set; }
    public District? District { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int ViewCount { get; set; }
    public ICollection<ListingAttributeValue> AttributeValues { get; set; } = new List<ListingAttributeValue>();
    public ICollection<ListingImage> Images { get; set; } = new List<ListingImage>();
}
