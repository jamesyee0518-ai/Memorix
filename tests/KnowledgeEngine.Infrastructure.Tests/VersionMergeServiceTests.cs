using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Audio;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class VersionMergeServiceTests
{
    private static async Task<AppDbContext> CreateDbAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static TranscriptionVersion MakeVersion(
        Guid jobId,
        string segmentUuid,
        string version,
        string text,
        DateTime createdAt,
        Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(),
        TranscriptionJobId = jobId,
        SegmentUuid = segmentUuid,
        Version = version,
        ParentVersionId = parentId,
        Text = text,
        ProviderId = "whisper-cpp",
        ModelId = "base",
        CreatedBy = "test",
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task MergeAsync_NoUserEditNoServerVersion_ReturnsBaseline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-001";
        var baseline = MakeVersion(
            jobId, segmentUuid, SegmentVersions.RawModel,
            "hello world", DateTime.UtcNow.AddMinutes(-10));
        db.TranscriptionVersions.Add(baseline);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var merged = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        Assert.Equal("hello world", merged.Text);
        Assert.Equal(baseline.Id, merged.ParentVersionId);
        Assert.Equal(SegmentVersions.Merged, merged.Version);
    }

    [Fact]
    public async Task MergeAsync_NoUserEdit_TakesServerVersionVerbatim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-002";
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var baseline = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, "hello world", t0);
        var server = MakeVersion(
            jobId, segmentUuid, SegmentVersions.ServerRetranscribed,
            "hello world improved", t0.AddMinutes(1), parentId: baseline.Id);
        db.TranscriptionVersions.AddRange(baseline, server);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var merged = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        Assert.Equal("hello world improved", merged.Text);
        Assert.Equal(server.Id, merged.ParentVersionId);
        Assert.Equal(SegmentVersions.Merged, merged.Version);
    }

    [Fact]
    public async Task MergeAsync_NoServerVersion_TakesUserEditVerbatim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-003";
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var baseline = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, "hello world", t0);
        var userEdit = MakeVersion(
            jobId, segmentUuid, SegmentVersions.UserEdited,
            "hello world user fix", t0.AddMinutes(1), parentId: baseline.Id);
        db.TranscriptionVersions.AddRange(baseline, userEdit);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var merged = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        Assert.Equal("hello world user fix", merged.Text);
        Assert.Equal(userEdit.Id, merged.ParentVersionId);
        Assert.Equal(SegmentVersions.Merged, merged.Version);
    }

    [Fact]
    public async Task MergeAsync_BothPresent_HighSimilarity_PerformsLineLevelMerge()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-004";
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        // Baseline: 5 lines. User changes line 2 (B -> B2). Server changes line 4 (D -> D2).
        // Server text is very similar to baseline (high bigram-Dice similarity > 0.6),
        // so a line-level three-way merge is performed.
        // Expected merged: A, B2, C, D2, E (user-only change taken, server-only change taken).
        var baselineText = "A\nB\nC\nD\nE";
        var userText = "A\nB2\nC\nD\nE";
        var serverText = "A\nB\nC\nD2\nE";

        var baseline = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, baselineText, t0);
        var userEdit = MakeVersion(
            jobId, segmentUuid, SegmentVersions.UserEdited, userText, t0.AddMinutes(1), parentId: baseline.Id);
        var server = MakeVersion(
            jobId, segmentUuid, SegmentVersions.ServerRetranscribed, serverText, t0.AddMinutes(2), parentId: baseline.Id);
        db.TranscriptionVersions.AddRange(baseline, userEdit, server);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var merged = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        Assert.Equal("A\nB2\nC\nD2\nE", merged.Text);
        Assert.Equal(userEdit.Id, merged.ParentVersionId);
        Assert.Equal(SegmentVersions.Merged, merged.Version);
    }

    [Fact]
    public async Task MergeAsync_BothPresent_LowSimilarity_PreservesUserEdit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-005";
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        // Server text shares no bigrams with baseline (similarity = 0 < 0.6 threshold).
        // The server is considered to have re-segmented the audio, so the user's
        // edit is preserved wholesale.
        var baselineText = "hello world";
        var userText = "hello world edited by user";
        var serverText = "zzzzzzzzzzzzzzzzzzzz";

        var baseline = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, baselineText, t0);
        var userEdit = MakeVersion(
            jobId, segmentUuid, SegmentVersions.UserEdited, userText, t0.AddMinutes(1), parentId: baseline.Id);
        var server = MakeVersion(
            jobId, segmentUuid, SegmentVersions.ServerRetranscribed, serverText, t0.AddMinutes(2), parentId: baseline.Id);
        db.TranscriptionVersions.AddRange(baseline, userEdit, server);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var merged = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        Assert.Equal(userText, merged.Text);
        Assert.Equal(userEdit.Id, merged.ParentVersionId);
        Assert.Equal(SegmentVersions.Merged, merged.Version);
    }

    [Fact]
    public async Task MergeAsync_AlreadyMerged_ReturnsExistingMergedVersion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-006";
        var t0 = DateTime.UtcNow.AddMinutes(-10);

        var baseline = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, "hello world", t0);
        var existingMerged = MakeVersion(
            jobId, segmentUuid, SegmentVersions.Merged,
            "hello world merged", t0.AddMinutes(5), parentId: baseline.Id);
        db.TranscriptionVersions.AddRange(baseline, existingMerged);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var result = await svc.MergeAsync(jobId, segmentUuid, CancellationToken.None);

        // Should return the existing MERGED version without creating a new one.
        Assert.Equal(existingMerged.Id, result.Id);
        Assert.Equal(SegmentVersions.Merged, result.Version);
        Assert.Equal("hello world merged", result.Text);

        // Verify no additional MERGED version was created.
        var mergedCount = await db.TranscriptionVersions
            .CountAsync(v => v.SegmentUuid == segmentUuid && v.Version == SegmentVersions.Merged);
        Assert.Equal(1, mergedCount);
    }

    [Fact]
    public async Task GetVersionHistoryAsync_ReturnsOrderedByCreatedAt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbAsync(connection);

        var jobId = Guid.NewGuid();
        var segmentUuid = "seg-007";
        var t0 = DateTime.UtcNow.AddMinutes(-30);

        // Insert out of order to verify ascending CreatedAt ordering.
        var v3 = MakeVersion(jobId, segmentUuid, SegmentVersions.ServerRetranscribed, "third", t0.AddMinutes(20));
        var v1 = MakeVersion(jobId, segmentUuid, SegmentVersions.RawModel, "first", t0);
        var v2 = MakeVersion(jobId, segmentUuid, SegmentVersions.UserEdited, "second", t0.AddMinutes(10));
        db.TranscriptionVersions.AddRange(v3, v1, v2);
        await db.SaveChangesAsync();

        var svc = new VersionMergeService(db, NullLogger<VersionMergeService>.Instance);
        var history = await svc.GetVersionHistoryAsync(segmentUuid, CancellationToken.None);

        Assert.Equal(3, history.Count);
        Assert.Equal(v1.Id, history[0].Id);
        Assert.Equal(v2.Id, history[1].Id);
        Assert.Equal(v3.Id, history[2].Id);
    }
}
