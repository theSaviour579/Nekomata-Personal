namespace Nekomata.AI.Schemas;

public static class ReplyTriageSchema
{
    public const string Json = """
    {"type":"object","additionalProperties":false,
    "required":["items"],
    "properties":{"items":{"type":"array","items":{"type":"object","additionalProperties":false,
      "required":["number","needsReply","gist"],
      "properties":{"number":{"type":"integer"},"needsReply":{"type":"boolean"},"gist":{"type":"string"}}}}}}
    """;
}
