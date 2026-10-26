using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GeneratedTextDeduplicatorTests
{
    [Fact]
    public void Removes_complete_repeated_response()
    {
        const string text = "First paragraph.\n\nSecond paragraph.\n\nFirst paragraph.\n\nSecond paragraph.";

        Assert.Equal($"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph.",
            GeneratedTextDeduplicator.RemoveRepeatedBlocks(text));
    }

    [Fact]
    public void Removes_adjacent_repeated_paragraph_but_preserves_distinct_content()
    {
        const string text = "Hello David.\n\nHello David.\n\nHere is the useful answer.";

        Assert.Equal($"Hello David.{Environment.NewLine}{Environment.NewLine}Here is the useful answer.",
            GeneratedTextDeduplicator.RemoveRepeatedBlocks(text));
    }

    [Fact]
    public void Preserves_non_repeated_blocks()
    {
        const string text = "One.\n\nTwo.\n\nThree.";

        Assert.Equal($"One.{Environment.NewLine}{Environment.NewLine}Two.{Environment.NewLine}{Environment.NewLine}Three.",
            GeneratedTextDeduplicator.RemoveRepeatedBlocks(text));
    }
}
