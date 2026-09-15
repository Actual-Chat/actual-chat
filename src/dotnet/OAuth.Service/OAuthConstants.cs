namespace ActualChat.OAuth;

public static class OAuthConstants
{
    public const string McpScope = "mcp";
    public const string SessionIdClaim = "sid";
    public const string RegisterRoute = "register";
    public const string AuthorizeRoute = "authorize";
    public const string TokenRoute = "token";
    public const string RevokeRoute = "revoke";
    public const string ConsentPath = "/oauth/consent";
    public const string DenyParameter = "voxt_deny";
    public const string McpResourcePath = "/api/mcp";

    public static class Properties
    {
        public const string RegisteredVia = "registered_via";
        public const string RegisteredAt = "registered_at";
        public const string CimdFetchedAt = "cimd_fetched_at";
        public const string SessionId = "sid";
    }

    public static class RegisteredVia
    {
        public const string Dcr = "dcr";
        public const string Cimd = "cimd";
    }
}
