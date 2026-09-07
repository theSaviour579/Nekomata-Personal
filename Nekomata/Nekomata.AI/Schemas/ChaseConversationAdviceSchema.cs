namespace Nekomata.AI.Schemas;

public static class ChaseConversationAdviceSchema
{
    public const string Name = "chase_conversation_advice";
    public const string Json = """
    {"type":"object","additionalProperties":false,
    "required":["acknowledgementMessageIds","summary","lastChaseMessageId","chaseQuote","promiseMessageId","promiseQuote","promisedDate","suggestedReply"],
    "properties":{"acknowledgementMessageIds":{"type":"array","items":{"type":"string"}},"summary":{"type":"string"},"lastChaseMessageId":{"type":"string"},"chaseQuote":{"type":"string"},"promiseMessageId":{"type":"string"},"promiseQuote":{"type":"string"},"promisedDate":{"type":"string"},"suggestedReply":{"type":"string"}}}
    """;
}
