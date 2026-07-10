namespace KnowledgeEngine.Application.Settings;

public class EmbeddingSettings
{
    public string Endpoint { get; set; } = "https://api.openai.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "text-embedding-3-small";
}
