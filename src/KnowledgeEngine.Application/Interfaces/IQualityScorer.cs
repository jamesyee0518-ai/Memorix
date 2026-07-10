namespace KnowledgeEngine.Application.Interfaces;

public interface IQualityScorer
{
    (int qualityScore, int valueScore, string? qualityReason) CalculateScores(
        string contentText,
        string? title,
        int wordCount,
        string sourceType,
        int? aiValueScore);
}
