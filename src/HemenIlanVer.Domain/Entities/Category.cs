using HemenIlanVer.Domain.Enums;

namespace HemenIlanVer.Domain.Entities;

public class Category : BaseEntity
{
    public Guid? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ListingType? DefaultListingType { get; set; }
    public ICollection<CategoryAttribute> Attributes { get; set; } = new List<CategoryAttribute>();
}
