namespace KnowledgeEngine.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string email, string role = "user");
    string GenerateMobileDeviceToken(Guid workspaceId, Guid deviceId, string clientId, DateTime expiresAt);
}
