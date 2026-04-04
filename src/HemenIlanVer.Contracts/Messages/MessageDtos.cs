namespace HemenIlanVer.Contracts.Messages;

public sealed record SendMessageRequest(Guid ListingId, string Body);
public sealed record MessageDto(Guid Id, Guid SenderUserId, string Body, DateTimeOffset CreatedAt, bool IsMine);
public sealed record ConversationDto(Guid ConversationId, Guid ListingId, string ListingTitle, IReadOnlyList<MessageDto> Messages);
