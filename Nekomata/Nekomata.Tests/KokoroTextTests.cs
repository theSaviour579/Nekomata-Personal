using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class KokoroTextTests
{
    [Theory]
    [InlineData("Two tickets are past S L A.", "Two tickets are past ess ell ay.")]
    [InlineData("MFA isn't set up for 3 users.", "em eff ay is not set up for 3 users.")]
    [InlineData("It's done, and you're clear until 2. I'd leave it.", "It is done, and you are clear until 2. I would leave it.")]
    [InlineData("Sam's laptop won't boot; we've logged it.", "Sams laptop will not boot; we have logged it.")]
    [InlineData("Two VPNs are down. OK.", "Two vee pee ens are down. okay.")]
    [InlineData("Very good, Sir. I am on it.", "Very good, Sir. I am on it.")]
    [InlineData("A ticket came in.", "A ticket came in.")]
    [InlineData("Microsoft Copilot licences are up for renewal.", "Microsoft co pilot licences are up for renewal.")]
    public void Prepares_text_kokoro_reads_properly(string input, string expected) =>
        Assert.Equal(expected, KokoroText.Prepare(input));
}
