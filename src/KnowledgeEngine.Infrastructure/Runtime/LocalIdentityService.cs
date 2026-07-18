using System.Security.Cryptography;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Runtime;

public sealed class LocalIdentityService : ILocalIdentityService
{
    private const string InstallationKeySetting = "identity.installation_key";
    private readonly IAppDbContext _db;
    private readonly IConfigService _configService;
    private readonly ICredentialStore _credentialStore;

    public LocalIdentityService(
        IAppDbContext db,
        IConfigService configService,
        ICredentialStore credentialStore)
    {
        _db = db;
        _configService = configService;
        _credentialStore = credentialStore;
    }

    public async Task<LocalIdentityDto> EnsureIdentityAsync(CancellationToken ct = default)
    {
        var config = await _configService.LoadConfigAsync(ct);
        var installationKey = config.Settings.TryGetValue(InstallationKeySetting, out var existingKey)
            ? existingKey
            : $"inst_{Guid.CreateVersion7():N}";

        var installation = await _db.LocalInstallations
            .FirstOrDefaultAsync(x => x.InstallationKey == installationKey, ct);
        var now = DateTime.UtcNow;
        if (installation == null)
        {
            installation = new LocalInstallation
            {
                Id = Guid.CreateVersion7(),
                InstallationKey = installationKey,
                Platform = GetPlatform(),
                DeviceName = Environment.MachineName,
                AppVersion = typeof(LocalIdentityService).Assembly.GetName().Version?.ToString() ?? string.Empty,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.LocalInstallations.Add(installation);
            config.Settings[InstallationKeySetting] = installationKey;
            await _configService.SaveConfigAsync(config, ct);
        }

        var profile = await _db.LocalProfiles
            .FirstOrDefaultAsync(x => x.InstallationId == installation.Id && x.Status == "active", ct);
        if (profile == null)
        {
            profile = new LocalProfile
            {
                Id = Guid.CreateVersion7(),
                InstallationId = installation.Id,
                DisplayName = "本地用户",
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.LocalProfiles.Add(profile);
        }

        var device = await _db.DeviceIdentities
            .FirstOrDefaultAsync(x => x.InstallationId == installation.Id && x.Status == "active", ct);
        if (device == null)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var deviceId = Guid.CreateVersion7();
            var keyRef = $"memorix/device/{deviceId:N}/private-key";
            await _credentialStore.SetAsync(
                keyRef,
                Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
                ct);

            device = new DeviceIdentity
            {
                Id = deviceId,
                InstallationId = installation.Id,
                DeviceKey = $"device_{deviceId:N}",
                PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                PrivateKeyRef = keyRef,
                KeyAlgorithm = "ecdsa-p256",
                LastSeenAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.DeviceIdentities.Add(device);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(device.PrivateKeyRef) ||
                await _credentialStore.GetAsync(device.PrivateKeyRef, ct) == null)
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                device.PrivateKeyRef = $"memorix/device/{device.Id:N}/private-key";
                device.PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
                device.KeyAlgorithm = "ecdsa-p256";
                await _credentialStore.SetAsync(
                    device.PrivateKeyRef,
                    Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
                    ct);
            }
            device.LastSeenAt = now;
            device.UpdatedAt = now;
        }

        installation.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        return new LocalIdentityDto
        {
            InstallationId = installation.Id,
            LocalProfileId = profile.Id,
            DeviceId = device.Id,
            InstallationKey = installation.InstallationKey,
            DeviceKey = device.DeviceKey,
            Platform = installation.Platform,
            DeviceName = installation.DeviceName
        };
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }
}
