using System.Text.Json;
using System.Text;
using Normyx.Application.Compliance;
using Normyx.Domain.Enums;

namespace Normyx.Infrastructure.Compliance;

public record PolicyRuleEvaluation(string RuleKey, string Description, FindingSeverity Severity, string[] OutputControlKeys);

public class PolicyEngine
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedPolicySet> _cache = new(StringComparer.Ordinal);

    public IReadOnlyCollection<PolicyRuleEvaluation> Evaluate(string rootPath, Dictionary<string, object?> facts)
    {
        var rules = LoadRules(rootPath);
        var evaluations = new List<PolicyRuleEvaluation>();

        foreach (var rule in rules)
        {
            if (!EvaluateCondition(rule.Condition, facts))
            {
                continue;
            }

            var severity = Enum.TryParse<FindingSeverity>(rule.Severity, true, out var parsed)
                ? parsed
                : FindingSeverity.Medium;

            evaluations.Add(new PolicyRuleEvaluation(
                rule.RuleKey,
                rule.Description,
                severity,
                rule.OutputControlKeys));
        }

        return evaluations;
    }

    private IReadOnlyList<PolicyRuleDocument> LoadRules(string rootPath)
    {
        var files = Directory.Exists(rootPath)
            ? Directory.GetFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : [];

        var snapshot = BuildSnapshot(files);

        lock (_sync)
        {
            if (_cache.TryGetValue(rootPath, out var existing) && existing.Snapshot == snapshot)
            {
                return existing.Rules;
            }

            var rules = ParseRules(files);
            _cache[rootPath] = new CachedPolicySet(snapshot, rules);
            return rules;
        }
    }

    private static string BuildSnapshot(string[] files)
    {
        if (files.Length == 0)
        {
            return "empty";
        }

        var builder = new StringBuilder();
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            builder.Append(file);
            builder.Append('|');
            builder.Append(info.LastWriteTimeUtc.Ticks);
            builder.Append('|');
            builder.Append(info.Length);
            builder.Append(';');
        }

        return builder.ToString();
    }

    private static IReadOnlyList<PolicyRuleDocument> ParseRules(IEnumerable<string> files)
    {
        var output = new List<PolicyRuleDocument>();
        foreach (var file in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var rule in rules.EnumerateArray())
            {
                if (!rule.TryGetProperty("condition", out var condition))
                {
                    continue;
                }

                var outputControls = rule.TryGetProperty("outputControlKeys", out var controls) && controls.ValueKind == JsonValueKind.Array
                    ? controls.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    : [];

                output.Add(new PolicyRuleDocument(
                    rule.GetProperty("ruleKey").GetString() ?? "UNKNOWN",
                    rule.GetProperty("description").GetString() ?? string.Empty,
                    rule.GetProperty("severity").GetString() ?? "Medium",
                    outputControls,
                    condition.Clone()));
            }
        }

        return output;
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

    private sealed record CachedPolicySet(string Snapshot, IReadOnlyList<PolicyRuleDocument> Rules);
    private sealed record PolicyRuleDocument(string RuleKey, string Description, string Severity, string[] OutputControlKeys, JsonElement Condition);
}
