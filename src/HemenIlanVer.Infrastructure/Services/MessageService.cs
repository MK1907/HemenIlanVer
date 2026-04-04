using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Messages;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class MessageService : IMessageService
{
    private readonly AppDbContext _db;

    public MessageService(AppDbContext db) => _db = db;

    public async Task<ConversationDto> GetOrCreateConversationAsync(Guid userId, Guid listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken)
            ?? throw new KeyNotFoundException("İlan bulunamadı.");

        if (listing.UserId == userId)
            throw new InvalidOperationException("Kendi ilanınıza mesaj gönderemezsiniz.");

        var conv = await _db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.ListingId == listingId && c.BuyerUserId == userId, cancellationToken);

        if (conv is null)
        {
            conv = new Conversation
            {
                Id = Guid.NewGuid(),
                ListingId = listingId,
                BuyerUserId = userId,
                SellerUserId = listing.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Conversations.Add(conv);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var messages = conv.Messages.OrderBy(m => m.CreatedAt).ToList();
        var dtos = messages.Select(m => new MessageDto(
            m.Id,
            m.SenderUserId,
            m.Body,
            m.CreatedAt,
            m.SenderUserId == userId)).ToList();

        return new ConversationDto(conv.Id, listingId, listing.Title, dtos);
    }

    public async Task<MessageDto> SendAsync(Guid userId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken)
            ?? throw new KeyNotFoundException("İlan bulunamadı.");

        if (listing.UserId == userId)
            throw new InvalidOperationException("Kendi ilanınıza mesaj gönderemezsiniz.");

        var conv = await _db.Conversations
            .FirstOrDefaultAsync(c => c.ListingId == request.ListingId && c.BuyerUserId == userId, cancellationToken);

        if (conv is null)
        {
            conv = new Conversation
            {
                Id = Guid.NewGuid(),
                ListingId = request.ListingId,
                BuyerUserId = userId,
                SellerUserId = listing.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.Conversations.Add(conv);
        }

        var msg = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            SenderUserId = userId,
            Body = request.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Messages.Add(msg);
        conv.LastMessageAt = msg.CreatedAt;
        await _db.SaveChangesAsync(cancellationToken);

        return new MessageDto(msg.Id, userId, msg.Body, msg.CreatedAt, true);
    }
}
