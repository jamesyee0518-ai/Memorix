using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Db;

public partial class AppDbContext
{
    private static void ConfigureMediaJob(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MediaJob>();
        entity.ToTable("media_jobs");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Capability).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Route).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ProviderId).HasMaxLength(128);
        entity.Property(x => x.ModelId).HasMaxLength(256);
        entity.Property(x => x.RunnerId).HasMaxLength(128);
        entity.Property(x => x.ParametersJson).IsRequired();
        entity.Property(x => x.InputAssetIdsJson).IsRequired();
        entity.Property(x => x.OutputAssetIdsJson).IsRequired();
        entity.Property(x => x.EventsJson).IsRequired();
        entity.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
        entity.HasIndex(x => new { x.Status, x.CreatedAt });
        entity.HasIndex(x => x.BillingJobId);
    }
}
