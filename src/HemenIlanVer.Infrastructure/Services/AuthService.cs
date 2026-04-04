using HemenIlanVer.Application.Abstractions;
using HemenIlanVer.Contracts.Auth;
using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Infrastructure.Identity;
using HemenIlanVer.Infrastructure.Options;
using HemenIlanVer.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HemenIlanVer.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly JwtOptions _jwtOpt;

    public AuthService(
        UserManager<ApplicationUser> users,
        AppDbContext db,
        IJwtTokenService jwt,
        IOptions<JwtOptions> jwtOpt)
    {
        _users = users;
        _db = db;
        _jwt = jwt;
        _jwtOpt = jwtOpt.Value;
    }

    public async Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            PhoneNumber = request.Phone,
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));

        await _users.AddToRoleAsync(user, "User");
        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(request.Email)
            ?? throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");
        if (!await _users.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedAccessException("Geçersiz e-posta veya şifre.");
        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var rt = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken && x.RevokedAt == null, cancellationToken)
            ?? throw new UnauthorizedAccessException("Geçersiz yenileme belirteci.");

        if (rt.ExpiresAt < DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Yenileme belirteci süresi dolmuş.");

        var user = await _users.FindByIdAsync(rt.UserId.ToString())
            ?? throw new UnauthorizedAccessException("Kullanıcı bulunamadı.");

        rt.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await IssueTokensAsync(user, cancellationToken);
    }

    private async Task<TokenResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var access = _jwt.CreateAccessToken(user);
        var refresh = _jwt.CreateRefreshToken();
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refresh,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOpt.RefreshTokenDays),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new TokenResponse(
            access,
            refresh,
            DateTimeOffset.UtcNow.AddMinutes(_jwtOpt.AccessTokenMinutes),
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName);
    }
}
