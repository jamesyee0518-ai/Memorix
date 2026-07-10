namespace KnowledgeEngine.Application.Settings;

/// <summary>
/// Settings for local file storage (Vault).
/// </summary>
public class LocalFileStorageSettings
{
    public string VaultRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "KnowledgeEngine", "Vault");
}
