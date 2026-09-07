using System.Security.Cryptography;
using System.Text;

namespace Nekomata.Core.Guardian.Anticipation;
public static class MeetingBriefEvidence
{
    public static string Signature(string subject, IEnumerable<string> attendees) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(subject.Trim().ToUpperInvariant() + "|" + string.Join("|", attendees.Select(x => x.Trim().ToUpperInvariant()).Distinct().Order()))));

    public static IReadOnlyList<string> Changes(IReadOnlyDictionary<string,string> before, IReadOnlyDictionary<string,string> after) =>
        after.Where(x => !before.TryGetValue(x.Key, out var previous) || previous != x.Value)
            .Select(x => (before.ContainsKey(x.Key) ? "Updated: " : "New in this brief: ") +
                (x.Value.Length <= 240 ? x.Value : x.Value[..240] + "… (see source preview)")).ToList();
}
