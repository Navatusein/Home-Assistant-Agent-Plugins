using HomeAssistant.Agent.Plugin.Discord.Contracts;
using HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;

namespace HomeAssistant.Agent.Plugin.Discord.Helpers;

public static class AuthTokenHelper
{
    public static AuthToken CreateFrom(string[] scopesRequired, TokenResponse tokenResponse)
    {
        return new AuthToken()
        {
            Token = tokenResponse.AccessToken,
            Scopes = scopesRequired,
            Expires = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn),
            RefreshToken = tokenResponse.RefreshToken,
            Type = tokenResponse.TokenType
        };
    }
}