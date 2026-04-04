namespace HemenIlanVer.Domain.Entities;

public class CategoryAttributeOption : BaseEntity
{
    public Guid CategoryAttributeId { get; set; }
    public CategoryAttribute CategoryAttribute { get; set; } = null!;
    public string ValueKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
