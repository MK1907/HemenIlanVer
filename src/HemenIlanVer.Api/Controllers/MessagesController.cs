using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Api.Extensions;
using HemenIlanVer.Contracts.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HemenIlanVer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly IMessageService _messages;

    public MessagesController(IMessageService messages) => _messages = messages;

    [HttpPost]
    public async Task<ActionResult<MessageDto>> Send([FromBody] SendMessageRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _messages.SendAsync(User.GetUserId(), request, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("conversation/{listingId:guid}")]
    public async Task<ActionResult<ConversationDto>> GetConversation(Guid listingId, CancellationToken ct)
    {
        try
        {
            return Ok(await _messages.GetOrCreateConversationAsync(User.GetUserId(), listingId, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
