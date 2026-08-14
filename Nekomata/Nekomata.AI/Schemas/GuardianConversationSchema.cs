namespace Nekomata.AI.Schemas;

public static class GuardianConversationSchema
{
    public const string Name =
        "guardian_conversation";

    public const string Json =
        """
{
  "type": "object",
  "additionalProperties": false,

"required": [
  "message",
  "actionType",
  "projectId",
  "tasks",
  "changes",
  "questions",
  "confidence"
],

  "properties": {

    "message": {
      "type": "string"
    },

    "actionType": {
      "type": "string"
    },

    "projectId": {
      "type": ["integer","null"]
    },

    "confidence": {
      "type": "integer"
    },

    "questions": {
      "type": "array",
      "items": {
        "type": "string"
      }
    },

   "tasks": {

  "type": "array",

  "items": {

    "type": "object",

    "additionalProperties": false,

    "required": [
      "title",
      "description",
      "priority",
      "estimatedMinutes",
      "estimatedBusinessValue",
      "requiresSql",
      "requiresFocus",
      "suggestedDelegate",
      "selected"
    ],

    "properties": {

      "title": {
        "type": "string"
      },

      "description": {
        "type": "string"
      },

      "priority": {
        "type": "string"
      },

      "estimatedMinutes": {
        "type": "integer"
      },

      "estimatedBusinessValue": {
        "type": "number"
      },

      "requiresSql": {
        "type": "boolean"
      },

      "requiresFocus": {
        "type": "boolean"
      },

      "suggestedDelegate": {
        "type": [
          "string",
          "null"
        ]
      },

      "selected": {
        "type": "boolean"
      }

    }

  }

},

    "changes": {

      "type": "array",

      "items": {

        "type": "object",

        "additionalProperties": false,

        "required": [
          "selected",
          "entityType",
          "entityId",
          "property",
          "oldValue",
          "newValue",
          "reason",
          "confidence"
        ],

        "properties": {

          "selected": {
            "type": "boolean"
          },

          "entityType": {
            "type": "string"
          },

          "entityId": {
            "type": "integer"
          },

          "property": {
            "type": "string"
          },

          "oldValue": {
            "type": "string"
          },

          "newValue": {
            "type": "string"
          },

          "reason": {
            "type": "string"
          },

          "confidence": {
            "type": "integer"
          }

        }

      }

    }

  }

}
""";
}