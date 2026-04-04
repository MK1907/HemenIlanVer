using HemenIlanVer.Contracts.Messages;

namespace HemenIlanVer.Application.Abstractions;

public interface IMessageService
{
    Task<ConversationDto> GetOrCreateConversationAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default);
    Task<MessageDto> SendAsync(Guid userId, SendMessageRequest request, CancellationToken cancellationToken = default);
}
