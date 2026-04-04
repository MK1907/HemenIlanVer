using HemenIlanVer.Contracts.Auth;

namespace HemenIlanVer.Application.Abstractions;

public interface IAuthService
{
    Task<TokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);
}
