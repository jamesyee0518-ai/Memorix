using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>Registers the media capabilities exposed by the isolated Python worker.</summary>
public static class MediaModelSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var models = new[]
        {
            New("minimax", "MiniMax-H3", "video.generate", "THIRD_PARTY_CLOUD", "USER_BYOK,PLATFORM_MANAGED", "SECOND@RESOLUTION", true),
            New("mlx", "MiniMax-H3-MLX-8bit", "video.generate", "LOCAL_DEVICE", "NO_CREDENTIAL", null, false),
            New("ollama", "qwen3-vl:32b", "vision.analyze", "LOCAL_DEVICE", "NO_CREDENTIAL", null, false),
            New("whisper", "large-v3-turbo", "speech.transcribe", "LOCAL_DEVICE", "NO_CREDENTIAL", null, false),
            New("cosyvoice", "CosyVoice", "speech.synthesize", "LOCAL_DEVICE", "NO_CREDENTIAL", null, false),
            New("elevenlabs", "eleven_multilingual_v2", "speech.synthesize", "THIRD_PARTY_CLOUD", "USER_BYOK,PLATFORM_MANAGED", "CHARACTER", true),
            New("deepseek", "deepseek-chat", "llm.chat", "THIRD_PARTY_CLOUD", "USER_BYOK,PLATFORM_MANAGED", "TOKEN", true),
        };
        foreach (var model in models)
        {
            if (await db.ModelRegistries.AnyAsync(x => x.ProviderId == model.ProviderId && x.ModelId == model.ModelId && x.Capability == model.Capability, ct)) continue;
            db.ModelRegistries.Add(model);
        }
        await db.SaveChangesAsync(ct);
    }

    private static ModelRegistry New(string provider, string model, string capability, string execution, string credentials, string? price, bool offDevice) => new()
    {
        Id = Guid.NewGuid(), ProviderId = provider, ModelId = model, DisplayName = model,
        Capability = capability, ExecutionModes = execution, CredentialModes = credentials,
        PricingUnit = price, SendsAudioOffDevice = offDevice, StoresProviderData = offDevice,
        IsEnabled = true, HealthStatus = ModelRegistryStatuses.Unknown,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };
}
