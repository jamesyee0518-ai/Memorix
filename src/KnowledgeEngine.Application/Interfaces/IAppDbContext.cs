using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Topic> Topics { get; }
    DbSet<Source> Sources { get; }
    DbSet<FileObject> Files { get; }
    DbSet<IngestJob> IngestJobs { get; }

    // Phase 2 entities
    DbSet<Document> Documents { get; }
    DbSet<DocumentChunk> DocumentChunks { get; }
    DbSet<Tag> Tags { get; }
    DbSet<DocumentTag> DocumentTags { get; }
    DbSet<Entity> Entities { get; }
    DbSet<DocumentEntity> DocumentEntities { get; }
    DbSet<EntityRelation> EntityRelations { get; }
    DbSet<AiJob> AiJobs { get; }

    // Phase 3 entities
    DbSet<SearchLog> SearchLogs { get; }
    DbSet<QaSession> QaSessions { get; }
    DbSet<QaMessage> QaMessages { get; }
    DbSet<RetrievalLog> RetrievalLogs { get; }
    DbSet<DocumentProcessingLog> DocumentProcessingLogs { get; }

    // Phase 4 entities
    DbSet<Report> Reports { get; }
    DbSet<ReportTemplate> ReportTemplates { get; }
    DbSet<ReportJob> ReportJobs { get; }
    DbSet<ReportSource> ReportSources { get; }
    DbSet<ExportJob> ExportJobs { get; }
    DbSet<ExportFile> ExportFiles { get; }
    DbSet<AgentProfile> AgentProfiles { get; }
    DbSet<AgentInvocationLog> AgentInvocationLogs { get; }
    DbSet<ReportCitation> ReportCitations { get; }

    // Phase 4 data-layer entities (tags/entities/embeddings/vector index)
    DbSet<ChunkEmbedding> ChunkEmbeddings { get; }
    DbSet<VectorIndexState> VectorIndexStates { get; }

    // Phase 5 entities
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<ApiCallLog> ApiCallLogs { get; }
    DbSet<UserUsageDaily> UserUsageDaily { get; }
    DbSet<BetaUser> BetaUsers { get; }
    DbSet<FeedbackItem> FeedbackItems { get; }

    // Phase 7 entities
    DbSet<ReleaseNote> ReleaseNotes { get; }

    // Dual-mode foundation entities
    DbSet<Workspace> Workspaces { get; }
    DbSet<InboxItem> InboxItems { get; }
    DbSet<InboxAttachment> InboxAttachments { get; }
    DbSet<ImportJob> ImportJobs { get; }
    DbSet<InboxEvent> InboxEvents { get; }
    DbSet<SyncCursor> SyncCursors { get; }
    DbSet<CloudInboxSyncLog> CloudInboxSyncLogs { get; }
    DbSet<MobileDevice> MobileDevices { get; }
    DbSet<PushNotification> PushNotifications { get; }
    DbSet<WorkspaceSetting> WorkspaceSettings { get; }
    DbSet<Terminology> Terminology { get; }
    DbSet<ChunkLocalization> ChunkLocalizations { get; }
    DbSet<ChunkEnrichment> ChunkEnrichments { get; }
    DbSet<MultilingualBatchJob> MultilingualBatchJobs { get; }
    DbSet<LocalInstallation> LocalInstallations { get; }
    DbSet<LocalProfile> LocalProfiles { get; }
    DbSet<DeviceIdentity> DeviceIdentities { get; }
    DbSet<CloudAccountBinding> CloudAccountBindings { get; }
    DbSet<WorkspaceBinding> WorkspaceBindings { get; }
    DbSet<SyncInboxStaging> SyncInboxStaging { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
