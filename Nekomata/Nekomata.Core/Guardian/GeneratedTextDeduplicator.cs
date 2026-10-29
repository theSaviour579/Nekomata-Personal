using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>Removes adjacent paragraphs, lines or sentences occasionally repeated by a text model.</summary>
public static partial class GeneratedTextDeduplicator
{
    public static string RemoveRepeatedBlocks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value?.Trim() ?? string.Empty;

        var cleaned = RemoveRepeatedSequence(value.Trim().ReplaceLineEndings(Environment.NewLine), ParagraphBreak(), Environment.NewLine + Environment.NewLine);
        cleaned = RemoveRepeatedSequence(cleaned, LineBreak(), Environment.NewLine);
        cleaned = RemoveRepeatedSequence(cleaned, SentenceBreak(), " ");
        return cleaned;
    }

    private static string RemoveRepeatedSequence(string value, Regex separator, string joinWith)
    {
        var blocks = separator.Split(value).Where(block => !string.IsNullOrWhiteSpace(block)).ToList();
        if (blocks.Count == 0) return string.Empty;
        var changed = false;

        // A model can return the complete answer twice, separated by paragraphs, ordinary lines, or sentences.
        if (blocks.Count % 2 == 0)
        {
            var half = blocks.Count / 2;
            if (blocks.Take(half).Sum(block => Canonical(block).Length) >= 20 &&
                Enumerable.Range(0, half).All(index => Equivalent(blocks[index], blocks[index + half])))
            {
                blocks = blocks.Take(half).ToList();
                changed = true;
            }
        }

        var result = new List<string>(blocks.Count);
        foreach (var block in blocks)
        {
            if (result.Count == 0 || Canonical(block).Length < 8 || !Equivalent(result[^1], block))
                result.Add(block.Trim());
            else
                changed = true;
        }
        return changed ? string.Join(joinWith, result) : value;
    }

    private static bool Equivalent(string left, string right) =>
        Canonical(left).Equals(Canonical(right), StringComparison.OrdinalIgnoreCase);

    private static string Canonical(string value) =>
        EdgeFormatting().Replace(Whitespace().Replace(value, " ").Trim(), string.Empty)
            .Trim().TrimEnd('.', '!', '?', ':', ';');

    [GeneratedRegex(@"(?:\r?\n){2,}")]
    private static partial Regex ParagraphBreak();
    [GeneratedRegex(@"\r?\n")]
    private static partial Regex LineBreak();
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBreak();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"^[\s>*#\-–—•·]+")]
    private static partial Regex EdgeFormatting();
}
