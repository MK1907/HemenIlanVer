namespace HemenIlanVer.Contracts.Auth;

public sealed record RegisterRequest(string Email, string Password, string? Phone, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, Guid UserId, string Email, string DisplayName);
public sealed record RefreshRequest(string RefreshToken);
