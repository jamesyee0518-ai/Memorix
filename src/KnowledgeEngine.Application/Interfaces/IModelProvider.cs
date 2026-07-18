namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Unified model provider abstraction.
/// Combines chat (LLM) and embedding capabilities behind a single interface.
/// Business code calls modelProvider.ChatAsync() / modelProvider.EmbedAsync()
/// without knowing whether it's Ollama, LM Studio, OpenAI, etc.
/// </summary>
public interface IModelProvider
{
    /// <summary>
    /// Chat completion (LLM dialogue).
    /// </summary>
    Task<LlmResult> ChatAsync(string systemPrompt, string userPrompt, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Generate embedding for a single text.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generate embeddings for a batch of texts.
    /// </summary>
    Task<List<float[]>> EmbedBatchAsync(List<string> texts, CancellationToken ct = default);

    /// <summary>
    /// Check if the model service is reachable.
    /// </summary>
    Task<ModelHealthStatus> HealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// The provider name (e.g. "lmstudio", "ollama", "openai").
    /// </summary>
    string ProviderName { get; }
}

public class ModelHealthStatus
{
    public string Status { get; set; } = "unknown";
    public string Provider { get; set; } = string.Empty;
    public string? ChatModel { get; set; }
    public string? EmbeddingModel { get; set; }
    public string? ErrorMessage { get; set; }
}
