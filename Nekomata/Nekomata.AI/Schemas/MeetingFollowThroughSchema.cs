namespace Nekomata.AI.Schemas;
public static class MeetingFollowThroughSchema
{
    public const string Json = """
    {"type":"object","additionalProperties":false,"required":["items","questions"],"properties":{
    "questions":{"type":"array","items":{"type":"string"}},
    "items":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["kind","title","owner","dueDate","evidence"],"properties":{
    "kind":{"type":"string","enum":["Action","Decision"]},"title":{"type":"string"},"owner":{"type":"string"},"dueDate":{"type":"string"},"evidence":{"type":"string"}}}}}}
    """;
}
