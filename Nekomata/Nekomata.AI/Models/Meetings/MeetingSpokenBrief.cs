namespace Nekomata.AI.Models.Meetings;

// A pre-meeting brief condensed for speech: two or three sentences Guardian says aloud
// a few minutes before the meeting starts.
public sealed class MeetingSpokenBrief
{
    public string Spoken { get; set; } = "";
    public string Reasoning { get; set; } = "";
}
