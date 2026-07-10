namespace KnowledgeEngine.Application.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string email);
    string GenerateMobileDeviceToken(Guid workspaceId, Guid deviceId, string clientId, DateTime expiresAt);
}
