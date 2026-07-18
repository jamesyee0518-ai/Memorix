namespace KnowledgeEngine.Application.Security;

public static class PlatformRoles
{
    public const string PlatformAdmin = "platform_admin";
    public const string Operator = "operator";
    public const string Support = "support";
    public const string User = "user";
}

public static class AuthorizationPolicies
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string PlatformOperator = "PlatformOperator";
}
