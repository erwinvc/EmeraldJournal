// Services/CurrentUser.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

public interface ICurrentUser
{
    Task<string?> GetUserIdAsync();
}

public class CurrentUser : ICurrentUser
{
    private readonly AuthenticationStateProvider _auth;
    public CurrentUser(AuthenticationStateProvider auth) => _auth = auth;

    public async Task<string?> GetUserIdAsync()
    {
        var state = await _auth.GetAuthenticationStateAsync();
        return state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
