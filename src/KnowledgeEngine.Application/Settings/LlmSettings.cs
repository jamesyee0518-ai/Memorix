namespace KnowledgeEngine.Application.Settings;

public class LlmSettings
{
    public string Endpoint { get; set; } = "https://api.openai.com";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public int MaxTokens { get; set; } = 4096;
}
