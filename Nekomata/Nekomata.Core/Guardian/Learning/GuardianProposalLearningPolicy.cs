using System.Text.Json;
using System.Text.RegularExpressions;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Learning;

public sealed class GuardianProposalLearningPolicy
{
    public bool ShouldSuppress(string candidateTitle, IEnumerable<GuardianMemory> memories)
    {
        var candidate = Tokens(candidateTitle);
        if (candidate.Count == 0) return false;
        return memories.Where(IsRejectedSuggestion).Any(memory =>
        {
            var title = ReadMetadata(memory.Metadata, "originalTitle") ?? memory.Summary;
            var rejected = Tokens(title);
            if (rejected.Count == 0) return false;
            var overlap = candidate.Intersect(rejected).Count();
            return overlap >= 2 && overlap / (double)Math.Min(candidate.Count, rejected.Count) >= 0.65;
        });
    }

    public string BuildGuidance(IEnumerable<GuardianMemory> memories, long? projectId = null)
    {
        var relevant = memories.Where(memory => memory.Category.Equals("ProposalFeedback", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(memory => projectId is not null && memory.ProjectId == projectId)
            .ThenByDescending(memory => memory.CreatedAt)
            .Take(12)
            .Select(memory => memory.Detail ?? memory.Summary)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        return relevant.Count == 0
            ? "No prior proposal feedback is available."
            : "Respect this reviewed proposal feedback. Do not repeat removed suggestions unless current evidence materially changes; prefer recorded replacements:\n" +
              string.Join("\n", relevant.Select(x => $"- {x}"));
    }

    private static bool IsRejectedSuggestion(GuardianMemory memory)
    {
        if (!memory.Category.Equals("ProposalFeedback", StringComparison.OrdinalIgnoreCase)) return false;
        var disposition = ReadMetadata(memory.Metadata, "disposition");
        return disposition is not null && (disposition.Equals("Removed", StringComparison.OrdinalIgnoreCase) ||
                                           disposition.Equals("Replaced", StringComparison.OrdinalIgnoreCase)) ||
               memory.Summary.StartsWith("Removed proposal", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadMetadata(string? metadata, string property)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        try
        {
            using var json = JsonDocument.Parse(metadata);
            return json.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static HashSet<string> Tokens(string text) => Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+")
        .Select(match => match.Value)
        .Where(token => token.Length > 2 && token is not ("the" or "and" or "for" or "from" or "into" or "with" or "task" or "project" or "proposal" or "removed"))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
