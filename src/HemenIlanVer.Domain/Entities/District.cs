namespace HemenIlanVer.Domain.Entities;

public class District : BaseEntity
{
    public Guid CityId { get; set; }
    public City City { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}
