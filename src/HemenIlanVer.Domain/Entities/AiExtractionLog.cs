namespace HemenIlanVer.Domain.Entities;

public class AiExtractionLog : BaseEntity
{
    public Guid? UserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int? PromptLength { get; set; }
    public int? LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OutputSummary { get; set; }
}
