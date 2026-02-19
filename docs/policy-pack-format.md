# Policy Pack Format

Policy packs are JSON files loaded from `policy-packs/*.json`.

## Structure

```json
{
  "name": "GDPR Core",
  "version": "2026.1",
  "scope": "GDPR",
  "rules": [
    {
      "ruleKey": "GDPR-TRANSFER-001",
      "description": "Transfers outside EU require safeguards.",
      "severity": "High",
      "condition": { "field": "inventory.transfer_outside_eu", "operator": "eq", "value": true },
      "outputControlKeys": ["GDPR-TRANSFER-SAFEGUARDS"]
    }
  ]
}
```

## Condition language (MVP)

- Leaf condition: `field`, `operator`, `value`
- Boolean composition:
  - `{"op":"and","conditions":[...]} `
  - `{"op":"or","conditions":[...]} `
  - `{"op":"not","condition":{...}}`

## Operators

- `eq`, `neq`
- `contains`
- `gt`, `gte`, `lt`, `lte`

## Available facts (MVP)

- `inventory.personal_data`
- `inventory.special_category`
- `inventory.transfer_outside_eu`
- `inventory.missing_lawful_basis`
- `inventory.max_retention_days`
- `system.prohibited_pattern`
- `system.high_risk_domain`
- `questionnaire.automated_decision`
- `questionnaire.critical_sector`
