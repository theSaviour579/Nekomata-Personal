namespace Nekomata.AI.Schemas;

public static class MeetingAnalysisSchema
{
    public const string Name =
        "meeting_analysis";

    public const string Json =
        """
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "summary",
    "actions",
    "questions"
  ],
  "properties": {

    "summary": {
      "type": "string"
    },

    "questions": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },

    "actions": {

      "type": "array",

      "items": {

        "type": "object",

        "additionalProperties": false,

        "required": [
          "selected",
          "actionType",
          "targetType",
          "targetName",
          "property",
          "newValue",
          "reason"
        ],

        "properties": {

          "selected": {
            "type": "boolean"
          },

          "actionType": {
            "type": "string"
          },

          "targetType": {
            "type": "string"
          },

          "targetName": {
            "type": "string"
          },

          "property": {
            "type": "string"
          },

          "newValue": {
            "type": "string"
          },

          "reason": {
            "type": "string"
          }

        }

      }

    }

  }

}
""";
}