namespace KnowledgeEngine.Application.Interfaces;

public interface ILlmService
{
    Task<LlmResult> CompleteAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);
}

public class LlmResult
{
    public string Content { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Model { get; set; } = string.Empty;
}
