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
          "taskId",
          "projectId",
          "property",
          "newValue",
          "description",
          "priority",
          "estimatedMinutes",
          "dueAt",
          "estimatedBusinessValue",
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

          "taskId": { "type": ["integer", "null"] },

          "projectId": { "type": ["integer", "null"] },

          "property": {
            "type": "string"
          },

          "newValue": {
            "type": "string"
          },

          "description": { "type": "string" },

          "priority": {
            "type": "string",
            "enum": ["Low", "Normal", "High", "Critical"]
          },

          "estimatedMinutes": { "type": ["integer", "null"] },

          "dueAt": { "type": "string" },

          "estimatedBusinessValue": { "type": ["number", "null"] },

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
