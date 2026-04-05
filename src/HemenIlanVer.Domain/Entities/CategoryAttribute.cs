using HemenIlanVer.Domain.Enums;

namespace HemenIlanVer.Domain.Entities;

public class CategoryAttribute : BaseEntity
{
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string AttributeKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AttributeDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public string? ValidationJson { get; set; }
    public Guid? ParentAttributeId { get; set; }
    public CategoryAttribute? ParentAttribute { get; set; }
    public ICollection<CategoryAttributeOption> Options { get; set; } = new List<CategoryAttributeOption>();
}
