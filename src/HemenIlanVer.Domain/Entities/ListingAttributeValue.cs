namespace HemenIlanVer.Domain.Entities;

public class ListingAttributeValue : BaseEntity
{
    public Guid ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public Guid CategoryAttributeId { get; set; }
    public CategoryAttribute CategoryAttribute { get; set; } = null!;
    public string? ValueText { get; set; }
    public int? ValueInt { get; set; }
    public decimal? ValueDecimal { get; set; }
    public bool? ValueBool { get; set; }
    public string? ValueJson { get; set; }
}
