using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public sealed record LanguageDetectionResult(
    string PrimaryLanguage,
    double Confidence,
    IReadOnlyDictionary<string, double> Distribution,
    bool IsMultilingual)
{
    public string DistributionJson => System.Text.Json.JsonSerializer.Serialize(Distribution);
}

public sealed record ContentClassificationResult(
    string ContentType,
    string ProcessingRoute,
    bool LocalizationRequired);

public interface ILanguageDetectionService
{
    LanguageDetectionResult Detect(string? text);
}

public interface IContentClassificationService
{
    ContentClassificationResult Classify(string? content, LanguageDetectionResult language);
}

public interface IChineseNormalizationService
{
    string Normalize(string? text);
}

public sealed record L1LocalizationResult(
    string TitleZh,
    string SummaryZh,
    IReadOnlyList<string> KeywordsZh,
    string Model,
    string PromptVersion);

public interface IL1LocalizationService
{
    Task<L1LocalizationResult> LocalizeDocumentAsync(Guid documentId, CancellationToken ct = default);
}

public sealed record LocalizationQualityResult(int Score, IReadOnlyList<string> Issues, bool RequiresReview);

public interface ILocalizationQualityService
{
    LocalizationQualityResult Validate(string? sourceText, string? localizedText, IEnumerable<Terminology>? terminology = null);
}

public interface ITerminologyService
{
    Task<IReadOnlyList<Terminology>> ListAsync(Guid userId, string? query = null, CancellationToken ct = default);
    Task<Terminology> UpsertAsync(Guid userId, Terminology term, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ExpandQueryAsync(Guid userId, string query, CancellationToken ct = default);
}

public interface IChineseTokenizer
{
    string Tokenize(string? text, IEnumerable<string>? protectedTerms = null);
}

public sealed record FullTextSearchHit(Guid DocumentId, Guid ChunkId, double Rank, string Channel);

public interface IChineseFullTextIndexService
{
    Task EnsureCreatedAsync(CancellationToken ct = default);
    Task IndexDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<IReadOnlyList<FullTextSearchHit>> SearchAsync(Guid userId, string query, int limit, CancellationToken ct = default);
}

public interface IRetrievalFusionService
{
    List<SearchResultItem> Fuse(IReadOnlyDictionary<string, IReadOnlyList<SearchResultItem>> channels, int limit, int rankConstant = 60);
}

public interface IRerankerService
{
    Task<List<SearchResultItem>> RerankAsync(string query, List<SearchResultItem> candidates,
        int maxChunks, int maxPerDocument, CancellationToken ct = default);
}

public sealed record ChunkTranslationRequest(string LanguageCode = "zh-CN", bool Force = false, string TranslationType = "machine");
public sealed record ChunkBatchResult(int Total, int Succeeded, int Failed, IReadOnlyList<string> Errors);

public interface IChunkLocalizationService
{
    Task<ChunkLocalization> TranslateAsync(Guid userId, Guid chunkId, ChunkTranslationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ChunkLocalization>> ListAsync(Guid userId, Guid chunkId, CancellationToken ct = default);
    Task<ChunkLocalization> ReviewAsync(Guid userId, Guid chunkId, Guid localizationId, string headingLocalized,
        string contentLocalized, bool approved, CancellationToken ct = default);
    Task<ChunkBatchResult> TranslateDocumentAsync(Guid userId, Guid documentId, bool force = false,
        int maxChunks = 500, CancellationToken ct = default);
}

public interface IChunkEnrichmentService
{
    Task<ChunkEnrichment> EnrichAsync(Guid userId, Guid chunkId, bool force = false, CancellationToken ct = default);
    Task<IReadOnlyList<ChunkEnrichment>> ListAsync(Guid userId, Guid chunkId, CancellationToken ct = default);
    Task<ChunkBatchResult> EnrichDocumentAsync(Guid userId, Guid documentId, bool force = false,
        int maxChunks = 500, CancellationToken ct = default);
}

public interface IMultiVectorEmbeddingService
{
    Task<int> IndexChunkAsync(Guid userId, Guid chunkId, CancellationToken ct = default);
}

public interface IMultilingualBatchJobService
{
    Task<MultilingualBatchJob> EnqueueAsync(Guid userId, Guid documentId, string jobType, bool force,
        int maxChunks, CancellationToken ct = default);
    Task<MultilingualBatchJob?> GetAsync(Guid userId, Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<MultilingualBatchJob>> ListAsync(Guid userId, Guid? documentId = null, CancellationToken ct = default);
    Task<MultilingualBatchJob> PauseAsync(Guid userId, Guid jobId, CancellationToken ct = default);
    Task<MultilingualBatchJob> ResumeAsync(Guid userId, Guid jobId, CancellationToken ct = default);
    Task<MultilingualBatchJob> RetryAsync(Guid userId, Guid jobId, CancellationToken ct = default);
}
