using System.Reflection;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class MeetingDeletionAndPublishTests
{
    [Fact]
    public async Task Delete_RemovesDependentRecordsAndScopedFiles_AndLeavesOtherMeetings()
    {
        await using var db = await CreateDbAsync();
        var owner = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var meetingId = Guid.NewGuid();
        var otherMeetingId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var scopedDir = Path.Combine(Path.GetTempPath(), "memorix-meeting-tests", meetingId.ToString("D"));
        Directory.CreateDirectory(scopedDir);
        var scopedFile = Path.Combine(scopedDir, "audio.wav");
        var storedFile = Path.Combine(scopedDir, "stored.bin");
        await File.WriteAllBytesAsync(scopedFile, [1, 2, 3]);
        await File.WriteAllBytesAsync(storedFile, [4, 5]);
        var unsafeDir = Path.Combine(Path.GetTempPath(), "memorix-meeting-tests", "unrelated");
        Directory.CreateDirectory(unsafeDir);
        var unsafeFile = Path.Combine(unsafeDir, "keep.wav");
        await File.WriteAllBytesAsync(unsafeFile, [9]);
        var recordingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine", "recordings", meetingId.ToString("D"));
        Directory.CreateDirectory(recordingDir);
        var recordingFile = Path.Combine(recordingDir, "chunk_000.wav");
        await File.WriteAllBytesAsync(recordingFile, [7]);

        var sourceId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        db.Sources.Add(new Source
        {
            Id = sourceId,
            UserId = owner,
            SourceType = "meeting_transcript",
            Url = MeetingPublishingService.MeetingResourceUrl(meetingId),
            OriginalFileId = fileId,
            ImportedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Files.Add(new FileObject
        {
            Id = fileId,
            WorkspaceId = owner,
            Bucket = "knowledge-engine",
            ObjectKey = "meetings/audio.bin",
            LocalPath = storedFile,
            CreatedAt = now
        });
        db.Meetings.Add(Meeting(meetingId, owner, now));
        db.Meetings.Add(Meeting(otherMeetingId, otherUser, now));
        db.MeetingSpeakers.Add(new MeetingSpeaker
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            SpeakerKey = "SPEAKER_00",
            GlobalSpeakerId = "s0",
            CreatedAt = now
        });
        db.MeetingSpeakers.Add(new MeetingSpeaker
        {
            Id = Guid.NewGuid(),
            MeetingId = otherMeetingId,
            SpeakerKey = "SPEAKER_00",
            GlobalSpeakerId = "other",
            CreatedAt = now
        });
        db.ActionItems.Add(new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            TaskText = "send notes",
            CreatedAt = now
        });
        db.MeetingMinutesVersions.Add(new MeetingMinutesVersion
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            VersionNo = 1,
            Summary = "talked",
            CreatedAt = now
        });
        db.AudioAssets.Add(new AudioAsset
        {
            Id = assetId,
            SourceId = sourceId,
            MeetingId = meetingId,
            UserId = owner,
            OriginalFilePath = unsafeFile,
            NormalizedFilePath = scopedFile,
            SourceSha256 = "abc",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.RecordingChunks.Add(new RecordingChunk
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            LocalUri = recordingFile,
            CreatedAt = now
        });
        db.TranscriptionJobs.Add(new TranscriptionJob
        {
            Id = jobId,
            AudioAssetId = assetId,
            UserId = owner,
            ProviderId = "local",
            ModelId = "paraformer",
            DocumentId = documentId,
            CreatedAt = now
        });
        db.TranscriptionSegments.Add(new TranscriptionSegment
        {
            Id = Guid.NewGuid(),
            TranscriptionJobId = jobId,
            SegmentUuid = "seg-1",
            ProviderId = "local",
            ModelId = "paraformer",
            Text = "hello",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.TranscriptionVersions.Add(new TranscriptionVersion
        {
            Id = Guid.NewGuid(),
            TranscriptionJobId = jobId,
            SegmentUuid = "seg-1",
            Text = "hello",
            ProviderId = "local",
            ModelId = "paraformer",
            CreatedAt = now
        });
        db.Documents.Add(new Document
        {
            Id = documentId,
            SourceId = sourceId,
            UserId = owner,
            AudioAssetId = assetId,
            Title = "minutes",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = chunkId,
            DocumentId = documentId,
            SourceId = sourceId,
            UserId = owner,
            Content = "hello",
            ContentOriginal = "hello",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ChunkEmbeddings.Add(new ChunkEmbedding
        {
            Id = Guid.NewGuid(),
            ChunkId = chunkId,
            WorkspaceId = owner.ToString(),
            Provider = "local",
            Model = "embed",
            EmbeddingJson = "[0.1]",
            Status = "done",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var strangerDelete = await new MeetingService(db, new TestUser(otherUser), NullLogger<MeetingService>.Instance)
            .DeleteAsync(meetingId, CancellationToken.None);
        Assert.False(strangerDelete);
        Assert.True(File.Exists(scopedFile));
        Assert.True(await db.Meetings.AnyAsync(m => m.Id == meetingId));

        var service = new MeetingService(db, new TestUser(owner), NullLogger<MeetingService>.Instance);
        var deleted = await service.DeleteAsync(meetingId, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(await db.Meetings.AnyAsync(m => m.Id == meetingId));
        Assert.True(await db.Meetings.AnyAsync(m => m.Id == otherMeetingId));
        Assert.Equal(1, await db.MeetingSpeakers.CountAsync());
        Assert.Equal(0, await db.ActionItems.CountAsync());
        Assert.Equal(0, await db.MeetingMinutesVersions.CountAsync());
        Assert.Equal(0, await db.AudioAssets.CountAsync());
        Assert.Equal(0, await db.RecordingChunks.CountAsync());
        Assert.Equal(0, await db.TranscriptionJobs.CountAsync());
        Assert.Equal(0, await db.TranscriptionSegments.CountAsync());
        Assert.Equal(0, await db.TranscriptionVersions.CountAsync());
        Assert.Equal(0, await db.Documents.CountAsync());
        Assert.Equal(0, await db.DocumentChunks.CountAsync());
        Assert.Equal(0, await db.ChunkEmbeddings.CountAsync());
        Assert.Equal(0, await db.Sources.CountAsync());
        Assert.Equal(0, await db.Files.CountAsync());
        Assert.False(File.Exists(scopedFile));
        Assert.False(File.Exists(storedFile));
        Assert.False(Directory.Exists(recordingDir));
        Assert.True(File.Exists(unsafeFile));
    }

    [Fact]
    public async Task PublishMinutes_NotFoundForAnotherUser_AndAllowedForOwner()
    {
        await using var db = await CreateDbAsync();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var meetingId = Guid.NewGuid();
        var minutesId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Meetings.Add(Meeting(meetingId, owner, now));
        db.MeetingMinutesVersions.Add(new MeetingMinutesVersion
        {
            Id = minutesId,
            MeetingId = meetingId,
            VersionNo = 1,
            Summary = "summary",
            ContentJson = "{}",
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var strangerRepo = DispatchProxy.Create<IKnowledgeRepository, ThrowingRepositoryProxy>();
        var strangerService = new MeetingPublishingService(
            db, strangerRepo, NullLogger<MeetingPublishingService>.Instance, new TestUser(stranger));
        var denied = await strangerService.PublishMinutesAsync(meetingId, minutesId, stranger, CancellationToken.None);

        Assert.Equal("not_found", denied.Status);
        Assert.Equal(0, ((ThrowingRepositoryProxy)(object)strangerRepo).Calls);

        var ownerRepo = DispatchProxy.Create<IKnowledgeRepository, RecordingRepositoryProxy>();
        var ownerService = new MeetingPublishingService(
            db, ownerRepo, NullLogger<MeetingPublishingService>.Instance, new TestUser(owner));
        var published = await ownerService.PublishMinutesAsync(meetingId, minutesId, owner, CancellationToken.None);

        Assert.Equal("published", published.Status);
        var input = ((RecordingRepositoryProxy)(object)ownerRepo).LastSource;
        Assert.NotNull(input);
        Assert.Equal(MeetingPublishingService.MeetingResourceUrl(meetingId), input!.Url);
    }

    private static Meeting Meeting(Guid id, Guid userId, DateTime now) => new()
    {
        Id = id,
        Title = "weekly",
        CreatedBy = userId,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<AppDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class TestUser(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "owner@example.com";
        public bool IsAuthenticated => true;
    }

    private class ThrowingRepositoryProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }

    private class RecordingRepositoryProxy : DispatchProxy
    {
        public CreateSourceInput? LastSource { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IKnowledgeRepository.CreateSourceAsync))
            {
                LastSource = Assert.IsType<CreateSourceInput>(args?[0]);
                return Task.FromResult(new SourceDto { Id = Guid.NewGuid().ToString() });
            }
            if (targetMethod?.Name == nameof(IKnowledgeRepository.CreateDocumentAsync))
            {
                return Task.FromResult(new DocumentDto { Id = Guid.NewGuid().ToString() });
            }
            throw new InvalidOperationException($"{targetMethod?.Name} was not expected.");
        }
    }
}
