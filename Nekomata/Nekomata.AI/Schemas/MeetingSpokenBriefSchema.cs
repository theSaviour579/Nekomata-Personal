namespace Nekomata.AI.Schemas;

public static class MeetingSpokenBriefSchema
{
    public const string Json = """
    {"type":"object","additionalProperties":false,
    "required":["spoken","reasoning"],
    "properties":{"spoken":{"type":"string"},"reasoning":{"type":"string"}}}
    """;
}
