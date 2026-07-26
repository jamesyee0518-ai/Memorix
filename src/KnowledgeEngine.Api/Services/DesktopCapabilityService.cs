using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Api.Services;

/// <summary>
/// Resolves the desktop capabilities that are safe to expose to setup and settings pages.
/// Availability is controlled by server-side feature flags so the UI cannot enter a
/// partially implemented runtime mode merely by changing client state.
/// </summary>
public sealed class DesktopCapabilityService
{
    public const string CloudModeFlag = "Features:DesktopCloudModeEnabled";
    public const string HybridModeFlag = "Features:HybridModeEnabled";
    public const string CloudInboxFlag = "Features:CloudInboxEnabled";
    public const string MinimumCloudApiVersionKey = "Features:MinimumCloudApiVersion";

    private readonly IConfiguration _configuration;

    public DesktopCapabilityService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public DesktopCapabilitiesDto GetCapabilities()
    {
        var cloudEnabled = _configuration.GetValue(CloudModeFlag, false);
        var hybridEnabled = _configuration.GetValue(HybridModeFlag, false);
        var cloudInboxEnabled = _configuration.GetValue(CloudInboxFlag, true);
        var minimumCloudApiVersion =
            _configuration[MinimumCloudApiVersionKey]?.Trim() is { Length: > 0 } configuredVersion
                ? configuredVersion
                : "1.0";

        return new DesktopCapabilitiesDto
        {
            Modes =
            [
                new WorkspaceModeOption
                {
                    Mode = "local",
                    Label = "本地模式",
                    Description = "数据保存在本机，适合隐私资料和本地模型用户",
                    Available = true,
                    Status = "ready",
                    Badge = "可用"
                },
                new WorkspaceModeOption
                {
                    Mode = "cloud",
                    Label = "云端模式",
                    Description = cloudEnabled
                        ? "数据保存在云端，支持多设备访问"
                        : "完整云端工作区正在建设，可先使用云端收件箱连接能力",
                    Available = cloudEnabled,
                    Status = cloudEnabled ? "ready" : "coming_soon",
                    Badge = cloudEnabled ? "可用" : "即将支持",
                    Reason = cloudEnabled ? null : "cloud_runtime_not_ready",
                    RequiresAuthentication = true,
                    MinimumCloudApiVersion = minimumCloudApiVersion
                },
                new WorkspaceModeOption
                {
                    Mode = "hybrid",
                    Label = "混合模式",
                    Description = hybridEnabled
                        ? "本地保存主库，云端用于采集、备份和同步"
                        : "本地主库与云端双向同步仍处于开发阶段",
                    Available = hybridEnabled,
                    Status = hybridEnabled ? "beta" : "preview",
                    Badge = hybridEnabled ? "Beta" : "预览",
                    Reason = hybridEnabled ? null : "bidirectional_sync_not_ready",
                    RequiresAuthentication = true,
                    MinimumCloudApiVersion = minimumCloudApiVersion
                }
            ],
            CloudInbox = new DesktopFeatureCapabilityDto
            {
                Feature = "cloud_inbox",
                Available = cloudInboxEnabled,
                Status = cloudInboxEnabled ? "beta" : "coming_soon",
                Badge = cloudInboxEnabled ? "Beta" : "即将支持",
                Reason = cloudInboxEnabled ? null : "cloud_inbox_disabled",
                RequiresAuthentication = true
            },
            CheckedAt = DateTime.UtcNow
        };
    }

    public WorkspaceModeOption GetMode(string? mode)
    {
        var normalizedMode = mode?.Trim().ToLowerInvariant();
        return GetCapabilities().Modes.FirstOrDefault(option => option.Mode == normalizedMode)
            ?? throw new ArgumentException($"Unsupported workspace mode: {mode}", nameof(mode));
    }

    public bool IsModeAvailable(string? mode)
    {
        try
        {
            return GetMode(mode).Available;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
