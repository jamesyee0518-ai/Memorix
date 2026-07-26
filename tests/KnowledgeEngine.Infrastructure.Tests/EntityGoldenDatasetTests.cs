using System.Text.Json;
using KnowledgeEngine.Infrastructure.Processing;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class EntityGoldenDatasetTests
{
    [Fact]
    public async Task GoldenDataset_HasOneThousandUniqueBalancedAndValidRecords()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "entity-resolution-golden-v1.jsonl");
        Assert.True(File.Exists(path), $"Golden Dataset not found: {path}");
        var lines = (await File.ReadAllLinesAsync(path))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        Assert.Equal(1000, lines.Count);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var same = 0;
        var different = 0;
        var versionBoundaries = 0;
        var sameNameAmbiguities = 0;
        var bilingualOrAbbreviation = 0;
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("id", out var id));
            Assert.True(ids.Add(id.GetString()!));
            Assert.False(string.IsNullOrWhiteSpace(
                root.GetProperty("mention").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                root.GetProperty("entity_type").GetString()));
            Assert.True(root.GetProperty("expected_reason_codes").GetArrayLength() > 0);
            var reasons = root.GetProperty("expected_reason_codes")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToList();
            if (reasons.Contains("SAME_NAME_DIFFERENT_CONTEXT"))
                sameNameAmbiguities++;
            if (reasons.Contains("BILINGUAL_OR_ABBREVIATION"))
                bilingualOrAbbreviation++;

            var decision = root.GetProperty("expected_decision").GetString();
            if (decision == "SAME_ENTITY")
            {
                same++;
                Assert.True(root.TryGetProperty(
                    "expected_canonical_name", out _));
            }
            else if (decision == "DIFFERENT_ENTITY")
            {
                different++;
                Assert.True(root.TryGetProperty("candidate_name", out _));
                if (reasons.Contains("MODEL_VERSION_CONFLICT"))
                {
                    versionBoundaries++;
                    Assert.True(EntityCandidateResolver.HasVersionConflict(
                        root.GetProperty("mention").GetString()!,
                        root.GetProperty("candidate_name").GetString()!));
                }
            }
            else
            {
                Assert.Fail($"Unexpected decision: {decision}");
            }
        }

        Assert.Equal(600, same);
        Assert.Equal(400, different);
        Assert.True(versionBoundaries >= 100);
        Assert.True(sameNameAmbiguities >= 100);
        Assert.True(bilingualOrAbbreviation >= 100);
    }
}
