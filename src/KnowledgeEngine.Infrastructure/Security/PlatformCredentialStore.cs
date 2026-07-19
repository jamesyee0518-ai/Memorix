using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Security;

/// <summary>
/// Persists secrets in the operating-system credential service. The secret is
/// is stored using the native credential provider for each platform.
/// </summary>
public sealed class PlatformCredentialStore : ICredentialStore
{
    private const string ServiceName = "com.memorix.desktop";

    public async Task SetAsync(string keyRef, string secret, CancellationToken ct = default)
    {
        if (OperatingSystem.IsMacOS())
        {
            await RunAsync(
                "/usr/bin/security",
                ["add-generic-password", "-U", "-s", ServiceName, "-a", keyRef, "-w", secret],
                null,
                ct);
            return;
        }
        if (OperatingSystem.IsLinux())
        {
            await RunAsync(
                "secret-tool",
                ["store", "--label=Memorix", "service", ServiceName, "account", keyRef],
                secret,
                ct);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(ServiceName),
                DataProtectionScope.CurrentUser);
            var path = WindowsSecretPath(keyRef);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, encrypted, ct);
            return;
        }
        throw new PlatformNotSupportedException(
            "A persistent credential provider is not configured for this platform.");
    }

    public async Task<string?> GetAsync(string keyRef, CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return await RunAsync(
                    "/usr/bin/security",
                    ["find-generic-password", "-s", ServiceName, "-a", keyRef, "-w"],
                    null,
                    ct);
            }
            if (OperatingSystem.IsLinux())
            {
                return await RunAsync(
                    "secret-tool",
                    ["lookup", "service", ServiceName, "account", keyRef],
                    null,
                    ct);
            }
            if (OperatingSystem.IsWindows())
            {
                var path = WindowsSecretPath(keyRef);
                if (!File.Exists(path)) return null;
                var encrypted = await File.ReadAllBytesAsync(path, ct);
                var clear = ProtectedData.Unprotect(
                    encrypted,
                    Encoding.UTF8.GetBytes(ServiceName),
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(clear);
            }
            throw new PlatformNotSupportedException(
                "A persistent credential provider is not configured for this platform.");
        }
        catch (CredentialNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string keyRef, CancellationToken ct = default)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                await RunAsync(
                    "/usr/bin/security",
                    ["delete-generic-password", "-s", ServiceName, "-a", keyRef],
                    null,
                    ct);
                return;
            }
            if (OperatingSystem.IsLinux())
            {
                await RunAsync(
                    "secret-tool",
                    ["clear", "service", ServiceName, "account", keyRef],
                    null,
                    ct);
                return;
            }
            if (OperatingSystem.IsWindows())
            {
                var path = WindowsSecretPath(keyRef);
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            throw new PlatformNotSupportedException(
                "A persistent credential provider is not configured for this platform.");
        }
        catch (CredentialNotFoundException)
        {
            // Delete is idempotent.
        }
    }

    private static async Task<string> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? stdin,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = stdin != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start credential provider: {executable}");
        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            process.StandardInput.Close();
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).TrimEnd('\r', '\n');
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
        {
            if (stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new CredentialNotFoundException();
            }
            throw new InvalidOperationException(
                $"Credential provider exited with code {process.ExitCode}: {stderr}");
        }
        return stdout;
    }

    private static string WindowsSecretPath(string keyRef)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyRef)));
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Memorix",
            "Credentials",
            $"{hash}.bin");
    }

    private sealed class CredentialNotFoundException : Exception;
}
