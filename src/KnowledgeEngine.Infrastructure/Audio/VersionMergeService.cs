using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Three-way merge implementation of <see cref="IVersionMergeService"/>.
/// <para>
/// Reconciles a user's manual edits (<c>USER_EDITED</c>) with a server
/// re-transcription (<c>SERVER_RETRANSCRIBED</c>) against the original
/// <c>RAW_MODEL</c> baseline, producing a <c>MERGED</c> version.
/// </para>
/// <para>
/// Merge policy (user edits win on conflict):
/// <list type="bullet">
///   <item>No user edit, no server version -> merged equals baseline.</item>
///   <item>No user edit -> take server version verbatim.</item>
///   <item>No server version -> take user edit verbatim.</item>
///   <item>Both present and server re-segmented the audio (low textual similarity to baseline) -> preserve user edit wholesale.</item>
///   <item>Both present and textually aligned -> line-level three-way merge: local-only changes win, server-only changes are taken, conflicts resolved in favour of the user.</item>
/// </list>
/// </para>
/// </summary>
public class VersionMergeService : IVersionMergeService
{
    /// <summary>
    /// Below this bigram-Dice similarity between the server and baseline texts,
    /// the server is considered to have re-segmented the audio and the user's
    /// edit is preserved wholesale.
    /// </summary>
    private const double ResegmentationSimilarityThreshold = 0.6;

    /// <summary>
    /// Line counts above this value fall back to whole-text comparison instead
    /// of the O(n*m) LCS-based line merge, to bound runtime on very large inputs.
    /// </summary>
    private const int MaxLinesForDiffMerge = 1000;

    private const string CreatedBySystem = "merge-service";

    private readonly IAppDbContext _db;
    private readonly ILogger<VersionMergeService> _logger;

    public VersionMergeService(IAppDbContext db, ILogger<VersionMergeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionVersion> MergeAsync(
        Guid transcriptionJobId,
        string segmentUuid,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(segmentUuid))
        {
            throw new ArgumentException("segmentUuid is required.", nameof(segmentUuid));
        }

        var history = await GetVersionHistoryAsync(segmentUuid, ct);

        // Idempotency: return the existing MERGED version if one already exists.
        var existingMerged = history.FirstOrDefault(v => v.Version == SegmentVersions.Merged);
        if (existingMerged != null)
        {
            _logger.LogInformation(
                "Segment {SegmentUuid}: MERGED version already exists ({VersionId}), returning idempotently.",
                segmentUuid, existingMerged.Id);
            return existingMerged;
        }

        var baseline = history.FirstOrDefault(v => v.Version == SegmentVersions.RawModel)
            ?? history.OrderBy(v => v.CreatedAt).FirstOrDefault();

        if (baseline == null)
        {
            throw new InvalidOperationException(
                $"Cannot merge segment {segmentUuid}: no baseline (RAW_MODEL) version exists.");
        }

        var userVersion = await GetUserEditedVersionAsync(segmentUuid, ct);
        var serverVersion = await GetServerRetranscribedVersionAsync(segmentUuid, ct);

        var (mergedText, parentId) = ComputeMerge(baseline, userVersion, serverVersion);

        var merged = new TranscriptionVersion
        {
            Id = Guid.NewGuid(),
            TranscriptionJobId = transcriptionJobId,
            SegmentUuid = segmentUuid,
            Version = SegmentVersions.Merged,
            ParentVersionId = parentId,
            Text = mergedText,
            ProviderId = baseline.ProviderId,
            ModelId = baseline.ModelId,
            CreatedBy = CreatedBySystem,
            CreatedAt = DateTime.UtcNow,
        };

        _db.TranscriptionVersions.Add(merged);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Segment {SegmentUuid}: created MERGED version {VersionId} (parent: {ParentId}).",
            segmentUuid, merged.Id, parentId);

        return merged;
    }

    /// <inheritdoc/>
    public async Task<List<TranscriptionVersion>> GetVersionHistoryAsync(
        string segmentUuid,
        CancellationToken ct)
    {
        return await _db.TranscriptionVersions
            .Where(v => v.SegmentUuid == segmentUuid)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<TranscriptionVersion?> GetUserEditedVersionAsync(
        string segmentUuid,
        CancellationToken ct)
    {
        return await _db.TranscriptionVersions
            .Where(v => v.SegmentUuid == segmentUuid && v.Version == SegmentVersions.UserEdited)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<TranscriptionVersion?> GetServerRetranscribedVersionAsync(
        string segmentUuid,
        CancellationToken ct)
    {
        return await _db.TranscriptionVersions
            .Where(v => v.SegmentUuid == segmentUuid && v.Version == SegmentVersions.ServerRetranscribed)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Core merge decision logic. Returns the merged text and the parent version id.
    /// </summary>
    private (string MergedText, Guid ParentId) ComputeMerge(
        TranscriptionVersion baseline,
        TranscriptionVersion? userVersion,
        TranscriptionVersion? serverVersion)
    {
        // Case 1: no user edit and no server re-transcription -> merged equals baseline.
        if (userVersion == null && serverVersion == null)
        {
            return (baseline.Text, baseline.Id);
        }

        // Case 2: no user edit -> take server version verbatim.
        if (userVersion == null)
        {
            return (serverVersion!.Text, serverVersion.Id);
        }

        // Case 3: no server re-transcription -> take user edit verbatim.
        if (serverVersion == null)
        {
            return (userVersion.Text, userVersion.Id);
        }

        // Case 4: both present. Detect whether the server re-segmented the audio
        // (substantially different text from the baseline), in which case the
        // user's edit is preserved wholesale.
        var serverSimilarity = BigramDiceSimilarity(baseline.Text, serverVersion.Text);
        if (serverSimilarity < ResegmentationSimilarityThreshold)
        {
            _logger.LogInformation(
                "Segment {SegmentUuid}: server re-segmented audio (similarity {Similarity:F2} < {Threshold}); preserving user edit.",
                baseline.SegmentUuid, serverSimilarity, ResegmentationSimilarityThreshold);
            return (userVersion.Text, userVersion.Id);
        }

        // Case 5: both present and textually aligned -> line-level three-way merge.
        var merged = ThreeWayMerge(baseline.Text, userVersion.Text, serverVersion.Text);
        return (merged, userVersion.Id);
    }

    /// <summary>
    /// Performs a diff3-style line-level three-way merge.
    /// <para>
    /// Regions changed only by the user are taken from the user's text; regions
    /// changed only by the server are taken from the server's text; regions
    /// changed by both (conflict) are resolved in favour of the user.
    /// </para>
    /// </summary>
    private static string ThreeWayMerge(string baseText, string localText, string serverText)
    {
        var baseLines = SplitLines(baseText);
        var localLines = SplitLines(localText);
        var serverLines = SplitLines(serverText);

        // Fall back to whole-text comparison for very large inputs.
        if (baseLines.Count > MaxLinesForDiffMerge
            || localLines.Count > MaxLinesForDiffMerge
            || serverLines.Count > MaxLinesForDiffMerge)
        {
            return localText;
        }

        var localMatches = LcsMatchMap(baseLines, localLines); // baseIndex -> localIndex
        var serverMatches = LcsMatchMap(baseLines, serverLines); // baseIndex -> serverIndex

        // Anchors: base indices matched in BOTH local and server. These are lines
        // unchanged on both sides and serve as synchronization points.
        var anchors = localMatches.Keys
            .Intersect(serverMatches.Keys)
            .OrderBy(i => i)
            .ToList();

        var result = new List<string>();
        int prevBase = -1, prevLocal = -1, prevServer = -1;

        foreach (var anchor in anchors)
        {
            var localIdx = localMatches[anchor];
            var serverIdx = serverMatches[anchor];

            // Region between the previous anchor and this anchor.
            MergeRegion(
                result,
                baseLines, localLines, serverLines,
                prevBase + 1, anchor,
                prevLocal + 1, localIdx,
                prevServer + 1, serverIdx);

            // The anchor line itself is common to all three.
            result.Add(baseLines[anchor]);

            prevBase = anchor;
            prevLocal = localIdx;
            prevServer = serverIdx;
        }

        // Trailing region after the last anchor.
        MergeRegion(
            result,
            baseLines, localLines, serverLines,
            prevBase + 1, baseLines.Count,
            prevLocal + 1, localLines.Count,
            prevServer + 1, serverLines.Count);

        return string.Join("\n", result);
    }

    /// <summary>
    /// Merges a single region (exclusive start, exclusive end) bounded by anchors.
    /// </summary>
    private static void MergeRegion(
        List<string> result,
        List<string> baseLines, List<string> localLines, List<string> serverLines,
        int baseStart, int baseEnd,
        int localStart, int localEnd,
        int serverStart, int serverEnd)
    {
        var baseRegion = baseLines.GetRange(baseStart, Math.Max(0, baseEnd - baseStart));
        var localRegion = localLines.GetRange(localStart, Math.Max(0, localEnd - localStart));
        var serverRegion = serverLines.GetRange(serverStart, Math.Max(0, serverEnd - serverStart));

        var baseEmpty = baseRegion.Count == 0;
        var localSameAsBase = RegionsEqual(localRegion, baseRegion);
        var serverSameAsBase = RegionsEqual(serverRegion, baseRegion);
        var localSameAsServer = RegionsEqual(localRegion, serverRegion);

        if (localSameAsBase && serverSameAsBase)
        {
            // Unchanged on both sides.
            result.AddRange(baseRegion);
        }
        else if (localSameAsBase)
        {
            // Only the server changed -> take server.
            result.AddRange(serverRegion);
        }
        else if (serverSameAsBase)
        {
            // Only the user changed -> take local.
            result.AddRange(localRegion);
        }
        else if (localSameAsServer)
        {
            // Both changed identically -> take either.
            result.AddRange(localRegion);
        }
        else if (baseEmpty && !localSameAsServer)
        {
            // Pure insertion on both sides with different content -> conflict, user wins.
            result.AddRange(localRegion);
        }
        else
        {
            // Conflict: both sides changed differently -> user edit wins.
            result.AddRange(localRegion);
        }
    }

    /// <summary>Compares two line regions for equality.</summary>
    private static bool RegionsEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Computes the LCS of two line lists and returns a map from base-line index
    /// to the matched other-line index for every line in the common subsequence.
    /// </summary>
    private static Dictionary<int, int> LcsMatchMap(List<string> baseLines, List<string> otherLines)
    {
        var n = baseLines.Count;
        var m = otherLines.Count;
        var matches = new Dictionary<int, int>();

        if (n == 0 || m == 0)
        {
            return matches;
        }

        // DP table of LCS lengths.
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                if (string.Equals(baseLines[i], otherLines[j], StringComparison.Ordinal))
                {
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }
        }

        // Backtrack to recover the matched pairs.
        int ii = 0, jj = 0;
        while (ii < n && jj < m)
        {
            if (string.Equals(baseLines[ii], otherLines[jj], StringComparison.Ordinal))
            {
                matches[ii] = jj;
                ii++;
                jj++;
            }
            else if (dp[ii + 1, jj] >= dp[ii, jj + 1])
            {
                ii++;
            }
            else
            {
                jj++;
            }
        }

        return matches;
    }

    /// <summary>Splits text into lines, preserving content without the newline separators.</summary>
    private static List<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new List<string>();
        }

        // Normalize CRLF to LF before splitting.
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
    }

    /// <summary>
    /// Computes the bigram Dice similarity coefficient between two strings,
    /// ranging from 0.0 (no overlap) to 1.0 (identical). Used as a cheap,
    /// language-agnostic textual-similarity signal for re-segmentation detection.
    /// </summary>
    private static double BigramDiceSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);

        if (bigramsA.Count == 0 || bigramsB.Count == 0)
        {
            // Single-character strings: fall back to direct equality.
            return string.Equals(a, b, StringComparison.Ordinal) ? 1.0 : 0.0;
        }

        var setA = new HashSet<string>(bigramsA);
        var setB = new HashSet<string>(bigramsB);

        int intersection = 0;
        foreach (var bigram in setA)
        {
            if (setB.Contains(bigram)) intersection++;
        }

        return (2.0 * intersection) / (setA.Count + setB.Count);
    }

    /// <summary>Extracts the set of character bigrams from a string.</summary>
    private static List<string> GetBigrams(string s)
    {
        var bigrams = new List<string>(Math.Max(0, s.Length - 1));
        for (int i = 0; i < s.Length - 1; i++)
        {
            bigrams.Add(s.Substring(i, 2));
        }
        return bigrams;
    }
}
