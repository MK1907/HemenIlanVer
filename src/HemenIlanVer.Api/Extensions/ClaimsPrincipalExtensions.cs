using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace HemenIlanVer.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var s = user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out var id))
            throw new UnauthorizedAccessException();
        return id;
    }
}
