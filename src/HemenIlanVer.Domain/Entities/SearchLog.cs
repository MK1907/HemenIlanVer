namespace HemenIlanVer.Domain.Entities;

public class SearchLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string RawQuery { get; set; } = string.Empty;
    public string? ExtractedJson { get; set; }
    public int ResultCount { get; set; }
}
