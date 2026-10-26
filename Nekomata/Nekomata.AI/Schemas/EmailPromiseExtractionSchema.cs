namespace Nekomata.AI.Schemas;

public static class EmailPromiseExtractionSchema
{
    public const string Json = """
    {"type":"object","additionalProperties":false,
    "required":["promises","reasoning"],
    "properties":{
      "promises":{"type":"array","items":{"type":"object","additionalProperties":false,
        "required":["to","what","due","estimatedMinutes","subject"],
        "properties":{"to":{"type":"string"},"what":{"type":"string"},"due":{"type":"string"},
          "estimatedMinutes":{"type":"integer"},"subject":{"type":"string"}}}},
      "reasoning":{"type":"string"}}}
    """;
}
