namespace HemenIlanVer.Domain.Entities;

public class Message : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? ReadAt { get; set; }
}
