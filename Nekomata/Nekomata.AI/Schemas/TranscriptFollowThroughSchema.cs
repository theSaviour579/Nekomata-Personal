namespace Nekomata.AI.Schemas;

public static class TranscriptFollowThroughSchema
{
    private const string Action = """
        {"type":"object","additionalProperties":false,"required":["who","what","when"],
         "properties":{"who":{"type":"string"},"what":{"type":"string"},"when":{"type":"string"}}}
        """;

    public const string Json = """
    {"type":"object","additionalProperties":false,
    "required":["summary","decisions","userActions","othersActions","followUpEmail"],
    "properties":{
      "summary":{"type":"string"},
      "decisions":{"type":"array","items":{"type":"string"}},
      "userActions":{"type":"array","items":
    """ + Action + """
    },
      "othersActions":{"type":"array","items":
    """ + Action + """
    },
      "followUpEmail":{"type":"string"}}}
    """;
}
