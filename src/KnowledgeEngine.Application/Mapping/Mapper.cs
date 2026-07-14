using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Mapping;

public static class Mapper
{
    public static UserInfoResponse ToUserInfoResponse(User user)
    {
        return new UserInfoResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Nickname = user.Nickname,
            AvatarUrl = user.AvatarUrl,
            PlanCode = user.PlanCode,
            Status = user.Status,
            Timezone = user.Timezone,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt
        };
    }

    public static TopicResponse ToTopicResponse(Topic topic)
    {
        return new TopicResponse
        {
            Id = topic.Id,
            UserId = topic.UserId,
            Name = topic.Name,
            Description = topic.Description,
            Domain = topic.Domain,
            Visibility = topic.Visibility,
            Status = topic.Status,
            CreatedAt = topic.CreatedAt,
            UpdatedAt = topic.UpdatedAt
        };
    }

    public static TopicListItem ToTopicListItem(Topic topic, int documentCount, int pendingCount, int failedCount)
    {
        return new TopicListItem
        {
            Id = topic.Id,
            Name = topic.Name,
            Description = topic.Description,
            Domain = topic.Domain,
            Visibility = topic.Visibility,
            Status = topic.Status,
            DocumentCount = documentCount,
            PendingCount = pendingCount,
            FailedCount = failedCount,
            CreatedAt = topic.CreatedAt,
            UpdatedAt = topic.UpdatedAt
        };
    }

    public static TopicDetail ToTopicDetail(Topic topic, TopicStats stats)
    {
        return new TopicDetail
        {
            Id = topic.Id,
            UserId = topic.UserId,
            Name = topic.Name,
            Description = topic.Description,
            Domain = topic.Domain,
            Visibility = topic.Visibility,
            Status = topic.Status,
            CreatedAt = topic.CreatedAt,
            UpdatedAt = topic.UpdatedAt,
            Stats = stats
        };
    }

    public static SourceResponse ToSourceResponse(Source source)
    {
        return new SourceResponse
        {
            Id = source.Id,
            UserId = source.UserId,
            TopicId = source.TopicId,
            SourceType = source.SourceType,
            Title = source.Title,
            Url = source.Url,
            Domain = source.Domain,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            ImportedAt = source.ImportedAt,
            OriginalFileId = source.OriginalFileId,
            ContentHash = source.ContentHash,
            Status = source.Status,
            ErrorMessage = source.ErrorMessage,
            RetryCount = source.RetryCount,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    public static SourceListItem ToSourceListItem(Source source)
    {
        return new SourceListItem
        {
            Id = source.Id,
            TopicId = source.TopicId,
            SourceType = source.SourceType,
            Title = source.Title,
            Url = source.Url,
            Domain = source.Domain,
            Status = source.Status,
            ErrorMessage = source.ErrorMessage,
            RetryCount = source.RetryCount,
            ImportedAt = source.ImportedAt,
            CreatedAt = source.CreatedAt
        };
    }

    public static SourceDetail ToSourceDetail(Source source)
    {
        return new SourceDetail
        {
            Id = source.Id,
            UserId = source.UserId,
            TopicId = source.TopicId,
            SourceType = source.SourceType,
            Title = source.Title,
            Url = source.Url,
            Domain = source.Domain,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            ImportedAt = source.ImportedAt,
            OriginalFileId = source.OriginalFileId,
            RawText = source.RawText,
            ContentHash = source.ContentHash,
            Status = source.Status,
            ErrorMessage = source.ErrorMessage,
            RetryCount = source.RetryCount,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    public static JobResponse ToJobResponse(IngestJob job)
    {
        return new JobResponse
        {
            Id = job.Id,
            UserId = job.UserId,
            SourceId = job.SourceId,
            JobType = job.JobType,
            Status = job.Status,
            ErrorMessage = job.ErrorMessage,
            RetryCount = job.RetryCount,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt
        };
    }

    public static JobListItem ToJobListItem(IngestJob job)
    {
        return new JobListItem
        {
            Id = job.Id,
            SourceId = job.SourceId,
            JobType = job.JobType,
            Status = job.Status,
            RetryCount = job.RetryCount,
            CreatedAt = job.CreatedAt,
            FinishedAt = job.FinishedAt
        };
    }

    // ===== Phase 2 Mappers =====

    public static DocumentListItem ToDocumentListItem(Document doc)
    {
        return new DocumentListItem
        {
            Id = doc.Id,
            SourceId = doc.SourceId,
            TopicId = doc.TopicId,
            Title = doc.TitleZh ?? doc.Title,
            Summary = doc.SummaryZh ?? doc.Summary,
            TitleOriginal = doc.TitleOriginal ?? doc.Title,
            TitleZh = doc.TitleZh,
            SummaryZh = doc.SummaryZh,
            AiStatus = doc.AiStatus,
            ValueScore = doc.ValueScore,
            WordCount = doc.WordCount,
            ReadingTimeMinutes = doc.ReadingTimeMinutes,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
            SourceType = doc.SourceType,
            SourceDomain = doc.SourceDomain,
            QualityScore = doc.QualityScore,
            ParseStatus = doc.ParseStatus,
            CleanStatus = doc.CleanStatus,
            IndexStatus = doc.IndexStatus,
            TagStatus = doc.TagStatus,
            EntityStatus = doc.EntityStatus,
            EmbeddingStatus = doc.EmbeddingStatus,
            PrimaryLanguage = doc.PrimaryLanguage ?? doc.Language,
            IsMultilingual = doc.IsMultilingual,
            LocalizationLevel = doc.LocalizationLevel,
            LanguageDetectStatus = doc.LanguageDetectStatus,
            LocalizationStatus = doc.LocalizationStatus,
            LocalizedAt = doc.LocalizedAt,
            LocalizationQualityScore = doc.LocalizationQualityScore,
            LocalizationQualityIssues = doc.LocalizationQualityIssues,
            GlossaryVersion = doc.GlossaryVersion
        };
    }

    public static DocumentDetail ToDocumentDetail(
        Document doc,
        List<TagResponse>? tags = null,
        List<EntityInDocument>? entities = null)
    {
        return new DocumentDetail
        {
            Id = doc.Id,
            SourceId = doc.SourceId,
            UserId = doc.UserId,
            TopicId = doc.TopicId,
            Title = doc.Title,
            ContentMarkdown = doc.ContentMarkdown,
            ContentText = doc.ContentText,
            Language = doc.Language,
            TitleOriginal = doc.TitleOriginal,
            TitleZh = doc.TitleZh,
            SummaryZh = doc.SummaryZh,
            KeywordsZh = doc.KeywordsZh,
            PrimaryLanguage = doc.PrimaryLanguage ?? doc.Language,
            LanguageDistribution = doc.LanguageDistribution,
            IsMultilingual = doc.IsMultilingual,
            LocalizationStrategy = doc.LocalizationStrategy,
            LocalizationLevel = doc.LocalizationLevel,
            LanguageDetectStatus = doc.LanguageDetectStatus,
            LocalizationStatus = doc.LocalizationStatus,
            EnrichmentStatus = doc.EnrichmentStatus,
            FulltextIndexStatus = doc.FulltextIndexStatus,
            ContentHash = doc.ContentHash,
            LocalizationModel = doc.LocalizationModel,
            LocalizationPromptVersion = doc.LocalizationPromptVersion,
            LocalizedAt = doc.LocalizedAt,
            LocalizationQualityScore = doc.LocalizationQualityScore,
            LocalizationQualityIssues = doc.LocalizationQualityIssues,
            GlossaryVersion = doc.GlossaryVersion,
            WordCount = doc.WordCount,
            ReadingTimeMinutes = doc.ReadingTimeMinutes,
            Summary = doc.Summary,
            OneSentenceConclusion = doc.OneSentenceConclusion,
            KeyPoints = doc.KeyPoints,
            BusinessSignals = doc.BusinessSignals,
            TechnicalSignals = doc.TechnicalSignals,
            Risks = doc.Risks,
            Opportunities = doc.Opportunities,
            ReusableMaterials = doc.ReusableMaterials,
            ValueScore = doc.ValueScore,
            QualityScore = doc.QualityScore,
            AiStatus = doc.AiStatus,
            AiModel = doc.AiModel,
            PromptVersion = doc.PromptVersion,
            ProcessedAt = doc.ProcessedAt,
            // Phase 3: Source metadata
            SourceType = doc.SourceType,
            SourceUrl = doc.SourceUrl,
            SourceDomain = doc.SourceDomain,
            Author = doc.Author,
            PublishedAt = doc.PublishedAt,
            RecommendedTags = doc.RecommendedTags,
            // Phase 3: Scoring
            ValueScoreReason = doc.ValueScoreReason,
            ShouldDeepProcess = doc.ShouldDeepProcess,
            // Phase 3: Multi-stage status
            ParseStatus = doc.ParseStatus,
            CleanStatus = doc.CleanStatus,
            ChunkStatus = doc.ChunkStatus,
            IndexStatus = doc.IndexStatus,
            TagStatus = doc.TagStatus,
            EntityStatus = doc.EntityStatus,
            EmbeddingStatus = doc.EmbeddingStatus,
            // Phase 3: Parser metadata
            ParserName = doc.ParserName,
            ParserVersion = doc.ParserVersion,
            CleanerVersion = doc.CleanerVersion,
            // Phase 3: AI raw output
            AiRawOutput = doc.AiRawOutput,
            AiErrorMessage = doc.AiErrorMessage,
            Tags = tags ?? new List<TagResponse>(),
            Entities = entities ?? new List<EntityInDocument>(),
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    public static DocumentResponse ToDocumentResponse(Document doc)
    {
        return new DocumentResponse
        {
            Id = doc.Id,
            SourceId = doc.SourceId,
            TopicId = doc.TopicId,
            Title = doc.Title,
            AiStatus = doc.AiStatus,
            ValueScore = doc.ValueScore,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    public static ProcessingLogItem ToProcessingLogItem(DocumentProcessingLog log)
    {
        return new ProcessingLogItem
        {
            Id = log.Id,
            SourceId = log.SourceId,
            DocumentId = log.DocumentId,
            StepName = log.StepName,
            Status = log.Status,
            Message = log.Message,
            ErrorCode = log.ErrorCode,
            StartedAt = log.StartedAt,
            FinishedAt = log.FinishedAt,
            DurationMs = log.DurationMs,
            CreatedAt = log.CreatedAt
        };
    }

    public static TagResponse ToTagResponse(Tag tag, DocumentTag? docTag = null)
    {
        return new TagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Type = tag.Type,
            Description = tag.Description,
            Source = docTag?.Source ?? "ai",
            Confidence = docTag?.Confidence
        };
    }

    public static TagListItem ToTagListItem(Tag tag, int documentCount)
    {
        return new TagListItem
        {
            Id = tag.Id,
            Name = tag.Name,
            Type = tag.Type,
            Description = tag.Description,
            DocumentCount = documentCount,
            CreatedAt = tag.CreatedAt
        };
    }

    public static EntityListItem ToEntityListItem(Entity entity, int documentCount)
    {
        return new EntityListItem
        {
            Id = entity.Id,
            Name = entity.Name,
            EntityType = entity.EntityType,
            Description = entity.Description,
            DocumentCount = documentCount,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public static EntityDetail ToEntityDetail(Entity entity, List<RelatedDocument>? relatedDocuments = null)
    {
        return new EntityDetail
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Name = entity.Name,
            NormalizedName = entity.NormalizedName,
            EntityType = entity.EntityType,
            Description = entity.Description,
            Metadata = entity.Metadata,
            RelatedDocuments = relatedDocuments ?? new List<RelatedDocument>(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public static EntityInDocument ToEntityInDocument(Entity entity, DocumentEntity? docEntity = null)
    {
        return new EntityInDocument
        {
            Id = entity.Id,
            Name = entity.Name,
            EntityType = entity.EntityType,
            Description = entity.Description,
            MentionCount = docEntity?.MentionCount ?? 1,
            Confidence = docEntity?.Confidence,
            Evidence = docEntity?.Evidence
        };
    }

    public static AiJobListItem ToAiJobListItem(AiJob job)
    {
        return new AiJobListItem
        {
            Id = job.Id,
            UserId = job.UserId,
            JobType = job.JobType,
            TargetType = job.TargetType,
            TargetId = job.TargetId,
            Status = job.Status,
            Model = job.Model,
            InputTokens = job.InputTokens,
            OutputTokens = job.OutputTokens,
            CostEstimate = job.CostEstimate,
            RetryCount = job.RetryCount,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt
        };
    }

    public static AiJobResponse ToAiJobResponse(AiJob job)
    {
        return new AiJobResponse
        {
            Id = job.Id,
            UserId = job.UserId,
            JobType = job.JobType,
            TargetType = job.TargetType,
            TargetId = job.TargetId,
            Status = job.Status,
            Model = job.Model,
            PromptVersion = job.PromptVersion,
            InputTokens = job.InputTokens,
            OutputTokens = job.OutputTokens,
            CostEstimate = job.CostEstimate,
            ErrorMessage = job.ErrorMessage,
            RetryCount = job.RetryCount,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt
        };
    }

    // ===== Phase 3 Mappers =====

    public static QaSessionListItem ToQaSessionListItem(QaSession session)
    {
        return new QaSessionListItem
        {
            Id = session.Id,
            TopicId = session.TopicId,
            Title = session.Title,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };
    }

    public static QaSessionResponse ToQaSessionResponse(QaSession session)
    {
        return new QaSessionResponse
        {
            Id = session.Id,
            UserId = session.UserId,
            TopicId = session.TopicId,
            Title = session.Title,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt
        };
    }

    public static QaMessageResponse ToQaMessageResponse(
        QaMessage msg,
        List<Citation>? citations = null,
        RetrievalInfo? retrieval = null)
    {
        return new QaMessageResponse
        {
            Id = msg.Id,
            SessionId = msg.SessionId,
            Role = msg.Role,
            Content = msg.Content,
            Citations = citations ?? new List<Citation>(),
            Retrieval = retrieval,
            Model = msg.Model,
            InputTokens = msg.InputTokens,
            OutputTokens = msg.OutputTokens,
            LatencyMs = msg.LatencyMs,
            CreatedAt = msg.CreatedAt
        };
    }

    // ===== Phase 4 Mappers =====

    public static ReportListItem ToReportListItem(Report report)
    {
        return new ReportListItem
        {
            Id = report.Id,
            TopicId = report.TopicId,
            ReportType = report.ReportType,
            Title = report.Title,
            Status = report.Status,
            QualityScore = report.QualityScore,
            GeneratedByModel = report.GeneratedByModel,
            StartDate = report.StartDate,
            EndDate = report.EndDate,
            ExportStatus = report.ExportStatus,
            CitationCoverage = report.CitationCoverage,
            EvidenceCount = report.EvidenceCount,
            CreatedAt = report.CreatedAt,
            UpdatedAt = report.UpdatedAt
        };
    }

    public static ReportDetail ToReportDetail(
        Report report,
        List<Guid>? sourceDocumentIds = null,
        List<Guid>? sourceChunkIds = null,
        List<CitationItem>? citations = null)
    {
        return new ReportDetail
        {
            Id = report.Id,
            UserId = report.UserId,
            TopicId = report.TopicId,
            ReportType = report.ReportType,
            Title = report.Title,
            ContentMarkdown = report.ContentMarkdown,
            Summary = report.Summary,
            OneSentenceConclusion = report.OneSentenceConclusion,
            Query = report.Query,
            StartDate = report.StartDate,
            EndDate = report.EndDate,
            SourceDocumentIds = sourceDocumentIds ?? new List<Guid>(),
            SourceChunkIds = sourceChunkIds ?? new List<Guid>(),
            Citations = citations ?? new List<CitationItem>(),
            GeneratedByModel = report.GeneratedByModel,
            PromptVersion = report.PromptVersion,
            Status = report.Status,
            QualityScore = report.QualityScore,
            CitationCoverage = report.CitationCoverage,
            EvidenceCount = report.EvidenceCount,
            ExportStatus = report.ExportStatus,
            ErrorMessage = report.ErrorMessage,
            CreatedAt = report.CreatedAt,
            UpdatedAt = report.UpdatedAt
        };
    }

    public static ExportJobResponse ToExportJobResponse(ExportJob job)
    {
        return new ExportJobResponse
        {
            JobId = job.Id,
            Status = job.Status,
            ExportType = job.ExportType,
            TargetType = job.TargetType,
            TargetId = job.TargetId,
            CreatedAt = job.CreatedAt
        };
    }

    public static ExportJobDetail ToExportJobDetail(ExportJob job, string? downloadUrl = null)
    {
        return new ExportJobDetail
        {
            Id = job.Id,
            UserId = job.UserId,
            TopicId = job.TopicId,
            ExportType = job.ExportType,
            TargetType = job.TargetType,
            TargetId = job.TargetId,
            Status = job.Status,
            FileId = job.FileId,
            DownloadUrl = downloadUrl,
            ErrorMessage = job.ErrorMessage,
            RetryCount = job.RetryCount,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt
        };
    }

    // ===== Phase 5 Mappers =====

    public static ApiKeyListItem ToApiKeyListItem(ApiKey key, List<Guid>? allowedTopicIds, List<string>? allowedActions)
    {
        return new ApiKeyListItem
        {
            Id = key.Id,
            Name = key.Name,
            KeyPrefix = key.KeyPrefix,
            PermissionScope = key.PermissionScope,
            AllowedTopicIds = allowedTopicIds,
            AllowedActions = allowedActions,
            RateLimitPerMinute = key.RateLimitPerMinute,
            DailyQuota = key.DailyQuota,
            ExpiresAt = key.ExpiresAt,
            Status = key.Status,
            CreatedAt = key.CreatedAt,
            LastUsedAt = key.LastUsedAt
        };
    }

    public static FeedbackResponse ToFeedbackResponse(FeedbackItem feedback)
    {
        return new FeedbackResponse
        {
            Id = feedback.Id,
            UserId = feedback.UserId,
            FeedbackType = feedback.FeedbackType,
            Module = feedback.Module,
            Severity = feedback.Severity,
            Title = feedback.Title,
            Content = feedback.Content,
            RelatedEntityType = feedback.RelatedEntityType,
            RelatedEntityId = feedback.RelatedEntityId,
            Status = feedback.Status,
            Priority = feedback.Priority,
            CreatedAt = feedback.CreatedAt,
            UpdatedAt = feedback.UpdatedAt
        };
    }

    public static FeedbackListItem ToFeedbackListItem(FeedbackItem feedback)
    {
        return new FeedbackListItem
        {
            Id = feedback.Id,
            FeedbackType = feedback.FeedbackType,
            Module = feedback.Module,
            Severity = feedback.Severity,
            Title = feedback.Title,
            Status = feedback.Status,
            Priority = feedback.Priority,
            CreatedAt = feedback.CreatedAt
        };
    }

    public static UsageDailyItem ToUsageDailyItem(UserUsageDaily usage)
    {
        return new UsageDailyItem
        {
            Date = usage.UsageDate,
            ImportedCount = usage.ImportedCount,
            DocumentCount = usage.DocumentCount,
            SearchCount = usage.SearchCount,
            QaCount = usage.QaCount,
            ReportCount = usage.ReportCount,
            ExportCount = usage.ExportCount,
            ApiCallCount = usage.ApiCallCount,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            EmbeddingTokens = usage.EmbeddingTokens,
            StorageBytes = usage.StorageBytes
        };
    }
}
