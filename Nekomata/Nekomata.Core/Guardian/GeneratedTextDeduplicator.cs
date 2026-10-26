using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>Removes exact adjacent blocks occasionally repeated by a text model without rewriting its wording.</summary>
public static partial class GeneratedTextDeduplicator
{
    public static string RemoveRepeatedBlocks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value?.Trim() ?? string.Empty;

        var blocks = ParagraphBreak().Split(value.Trim()).Where(block => !string.IsNullOrWhiteSpace(block)).ToList();
        if (blocks.Count == 0) return string.Empty;

        // A model can return the complete answer twice. Remove the second copy before handling adjacent paragraphs.
        if (blocks.Count % 2 == 0)
        {
            var half = blocks.Count / 2;
            if (Enumerable.Range(0, half).All(index => Equivalent(blocks[index], blocks[index + half])))
                blocks = blocks.Take(half).ToList();
        }

        var result = new List<string>(blocks.Count);
        foreach (var block in blocks)
            if (result.Count == 0 || !Equivalent(result[^1], block)) result.Add(block.Trim());
        return string.Join(Environment.NewLine + Environment.NewLine, result);
    }

    private static bool Equivalent(string left, string right) =>
        Whitespace().Replace(left, " ").Trim().Equals(Whitespace().Replace(right, " ").Trim(), StringComparison.Ordinal);

    [GeneratedRegex(@"(?:\r?\n){2,}")]
    private static partial Regex ParagraphBreak();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
