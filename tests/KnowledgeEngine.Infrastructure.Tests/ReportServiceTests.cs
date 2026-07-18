using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class ReportServiceTests
{
    [Fact]
    public async Task DeleteAsync_RemovesOwnedReportAndDependentRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Reports.Add(new Report
        {
            Id = reportId, UserId = userId, ReportType = "topic", Title = "Test report",
            Status = "done", CreatedAt = now, UpdatedAt = now
        });
        db.ReportSources.Add(new ReportSource
        {
            ReportId = reportId, DocumentId = documentId, ChunkId = chunkId, CitationIndex = 1, CreatedAt = now
        });
        db.ReportCitations.Add(new ReportCitation
        {
            Id = Guid.NewGuid(), ReportId = reportId, DocumentId = documentId,
            CitationIndex = 1, CreatedAt = now
        });
        db.ReportJobs.Add(new ReportJob
        {
            Id = Guid.NewGuid(), UserId = userId, ReportId = reportId, ReportType = "topic",
            Status = "done", CreatedAt = now, UpdatedAt = now
        });
        db.ExportJobs.Add(new ExportJob
        {
            Id = Guid.NewGuid(), UserId = userId, TargetType = "report", TargetId = reportId,
            ExportType = "markdown", Status = "failed", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new ReportService(db, NullLogger<ReportService>.Instance);
        var result = await service.DeleteAsync(userId, reportId);

        Assert.True(result.Success);
        Assert.Empty(await db.Reports.Where(x => x.Id == reportId).ToListAsync());
        Assert.Empty(await db.ReportSources.Where(x => x.ReportId == reportId).ToListAsync());
        Assert.Empty(await db.ReportCitations.Where(x => x.ReportId == reportId).ToListAsync());
        Assert.Empty(await db.ReportJobs.Where(x => x.ReportId == reportId).ToListAsync());
        Assert.Empty(await db.ExportJobs.Where(x => x.TargetId == reportId).ToListAsync());
    }
}
