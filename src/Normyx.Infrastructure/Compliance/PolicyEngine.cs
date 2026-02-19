using System.Text.Json;
using Normyx.Application.Compliance;
using Normyx.Domain.Enums;

namespace Normyx.Infrastructure.Compliance;

public record PolicyRuleEvaluation(string RuleKey, string Description, FindingSeverity Severity, string[] OutputControlKeys);

public class PolicyEngine
{
    public IReadOnlyCollection<PolicyRuleEvaluation> Evaluate(string rootPath, Dictionary<string, object?> facts)
    {
        var filePaths = Directory.Exists(rootPath)
            ? Directory.GetFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly)
            : [];

        var evaluations = new List<PolicyRuleEvaluation>();

        foreach (var file in filePaths)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var rule in rules.EnumerateArray())
            {
                var condition = rule.GetProperty("condition");
                if (!EvaluateCondition(condition, facts))
                {
                    continue;
                }

                var severityRaw = rule.GetProperty("severity").GetString() ?? "Medium";
                var severity = Enum.TryParse<FindingSeverity>(severityRaw, true, out var parsed)
                    ? parsed
                    : FindingSeverity.Medium;

                var outputControls = rule.TryGetProperty("outputControlKeys", out var controls) && controls.ValueKind == JsonValueKind.Array
                    ? controls.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    : [];

                evaluations.Add(new PolicyRuleEvaluation(
                    rule.GetProperty("ruleKey").GetString() ?? "UNKNOWN",
                    rule.GetProperty("description").GetString() ?? string.Empty,
                    severity,
                    outputControls));
            }
        }

        return evaluations;
    }

    private static bool EvaluateCondition(JsonElement condition, Dictionary<string, object?> facts)
    {
        if (condition.TryGetProperty("op", out var opNode))
        {
            var op = opNode.GetString()?.ToLowerInvariant();

            if (op == "and")
            {
                return condition.TryGetProperty("conditions", out var conditions)
                    && conditions.EnumerateArray().All(x => EvaluateCondition(x, facts));
            }

            if (op == "or")
            {
                return condition.TryGetProperty("conditions", out var conditions)
                    && conditions.EnumerateArray().Any(x => EvaluateCondition(x, facts));
            }

            if (op == "not")
            {
                return condition.TryGetProperty("condition", out var inner) && !EvaluateCondition(inner, facts);
            }
        }

        var field = condition.GetProperty("field").GetString() ?? string.Empty;
        var opValue = condition.GetProperty("operator").GetString()?.ToLowerInvariant() ?? "eq";

        if (!facts.TryGetValue(field, out var left))
        {
            return false;
        }

        object? right = condition.TryGetProperty("value", out var valueNode)
            ? valueNode.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => valueNode.TryGetDouble(out var d) ? d : null,
                JsonValueKind.String => valueNode.GetString(),
                _ => null
            }
            : null;

        return opValue switch
        {
            "eq" => Equals(left?.ToString()?.ToLowerInvariant(), right?.ToString()?.ToLowerInvariant()),
            "neq" => !Equals(left?.ToString()?.ToLowerInvariant(), right?.ToString()?.ToLowerInvariant()),
            "contains" => left?.ToString()?.Contains(right?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            "gt" => TryNum(left, out var l1) && TryNum(right, out var r1) && l1 > r1,
            "gte" => TryNum(left, out var l2) && TryNum(right, out var r2) && l2 >= r2,
            "lt" => TryNum(left, out var l3) && TryNum(right, out var r3) && l3 < r3,
            "lte" => TryNum(left, out var l4) && TryNum(right, out var r4) && l4 <= r4,
            _ => false
        };
    }

    private static bool TryNum(object? value, out double number)
        => double.TryParse(value?.ToString(), out number);
}
