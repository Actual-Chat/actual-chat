

using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;

public class BotAuthenticationSchemeOptions: AuthenticationSchemeOptions {
    public SigningCredentials SigningCredentials{get; set;}
}