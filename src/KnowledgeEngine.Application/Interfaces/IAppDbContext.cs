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
    DbSet<EntityAlias> EntityAliases { get; }
    DbSet<EntityMention> EntityMentions { get; }
    DbSet<EntityExternalId> EntityExternalIds { get; }
    DbSet<EntityResolutionCandidate> EntityResolutionCandidates { get; }
    DbSet<EntityEmbedding> EntityEmbeddings { get; }
    DbSet<EntityGovernanceTask> EntityGovernanceTasks { get; }
    DbSet<EntityMergeLog> EntityMergeLogs { get; }
    DbSet<EntityMergeBlocklist> EntityMergeBlocklist { get; }
    DbSet<EntityOutboxEvent> EntityOutboxEvents { get; }
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

    // AI billing control-plane entities
    DbSet<BillingAccount> BillingAccounts { get; }
    DbSet<WorkspaceBillingBinding> WorkspaceBillingBindings { get; }
    DbSet<AccountEntitlement> AccountEntitlements { get; }
    DbSet<PricePlanVersion> PricePlanVersions { get; }
    DbSet<PriceRule> PriceRules { get; }
    DbSet<QuotaBucket> QuotaBuckets { get; }
    DbSet<BalanceReservation> BalanceReservations { get; }
    DbSet<AiTask> AiTasks { get; }
    DbSet<AiRequestAttempt> AiRequestAttempts { get; }
    DbSet<UsageEvent> UsageEvents { get; }
    DbSet<BillingCharge> BillingCharges { get; }
    DbSet<ProviderCost> ProviderCosts { get; }
    DbSet<AccountLedger> AccountLedger { get; }
    DbSet<RechargeProduct> RechargeProducts { get; }
    DbSet<RechargeOrder> RechargeOrders { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<PaymentNotification> PaymentNotifications { get; }
    DbSet<PaymentRefund> PaymentRefunds { get; }

    // Phase 6 - Audio capability entities
    DbSet<AudioAsset> AudioAssets { get; }
    DbSet<TranscriptionJob> TranscriptionJobs { get; }
    DbSet<TranscriptionSegment> TranscriptionSegments { get; }
    DbSet<ProviderCredential> ProviderCredentials { get; }
    DbSet<ProviderUsageRecord> ProviderUsageRecords { get; }
    DbSet<VoiceCloneConsent> VoiceCloneConsents { get; }
    DbSet<CorrectionDictionary> CorrectionDictionaries { get; }
    DbSet<TranscriptionVersion> TranscriptionVersions { get; }

    // Model Registry and Benchmark entities
    DbSet<ModelRegistry> ModelRegistries { get; }
    DbSet<BenchmarkResult> BenchmarkResults { get; }

    // Prompt Registry, Enterprise Policy, and A/B Test entities
    DbSet<PromptRegistry> PromptRegistries { get; }
    DbSet<EnterprisePolicy> EnterprisePolicies { get; }
    DbSet<PromptABTest> PromptABTests { get; }

    // LAN node discovery & provider marketplace entities
    DbSet<LanNode> LanNodes { get; }
    DbSet<ProviderMarketplaceEntry> ProviderMarketplaceEntries { get; }

    // Meeting entities (Phase 6 - Meeting lifecycle)
    DbSet<Meeting> Meetings { get; }
    DbSet<MeetingSpeaker> MeetingSpeakers { get; }
    DbSet<MeetingMinutesVersion> MeetingMinutesVersions { get; }
    DbSet<ActionItem> ActionItems { get; }
    DbSet<PseudonymMapping> PseudonymMappings { get; }
    DbSet<RecordingChunk> RecordingChunks { get; }
    DbSet<MeetingProcessingTask> MeetingProcessingTasks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
