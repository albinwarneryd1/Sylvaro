# AI JSON Schemas (MVP)

The AI layer always returns structured JSON and is validated before persistence.

## DraftActionPlanJson

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["actions"],
  "properties": {
    "actions": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": ["title", "description", "priority", "ownerRole", "acceptanceCriteria", "evidenceNeeded"],
        "properties": {
          "title": { "type": "string" },
          "description": { "type": "string" },
          "priority": { "type": "string" },
          "ownerRole": { "type": "string" },
          "acceptanceCriteria": { "type": "string" },
          "evidenceNeeded": { "type": "array", "items": { "type": "string" } }
        }
      }
    }
  }
}
```

## DraftDpiaJson

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["sections"],
  "properties": {
    "sections": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "required": ["title", "claims", "uncertainties"],
        "properties": {
          "title": { "type": "string" },
          "claims": { "type": "array", "items": { "type": "string" } },
          "uncertainties": { "type": "array", "items": { "type": "string" } }
        }
      }
    }
  }
}
```
