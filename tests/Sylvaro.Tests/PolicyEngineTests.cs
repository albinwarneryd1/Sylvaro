using System.Text;
using Normyx.Infrastructure.Compliance;
using Xunit;

namespace Normyx.Tests;

public class PolicyEngineTests
{
    [Fact]
    public void Evaluate_ReturnsMatchingRule()
    {
        var rootPath = CreateTempPolicyDirectory();
        try
        {
            WritePolicyFile(rootPath, """
                {
                  "rules": [
                    {
                      "ruleKey": "GDPR_PERSONAL_DATA",
                      "description": "Personal data handling requires controls",
                      "severity": "High",
                      "condition": { "field": "containsPersonalData", "operator": "eq", "value": true },
                      "outputControlKeys": ["CTRL-GDPR-01"]
                    }
                  ]
                }
                """);

            var engine = new PolicyEngine();
            var result = engine.Evaluate(rootPath, new Dictionary<string, object?>
            {
                ["containsPersonalData"] = true
            });

            var rule = Assert.Single(result);
            Assert.Equal("GDPR_PERSONAL_DATA", rule.RuleKey);
            Assert.Equal("High", rule.Severity.ToString());
            Assert.Contains("CTRL-GDPR-01", rule.OutputControlKeys);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void Evaluate_RefreshesCacheWhenPolicyFileChanges()
    {
        var rootPath = CreateTempPolicyDirectory();
        try
        {
            WritePolicyFile(rootPath, """
                {
                  "rules": [
                    {
                      "ruleKey": "RUNTIME_CACHE_CHECK",
                      "description": "Cache should invalidate on file content change",
                      "severity": "Medium",
                      "condition": { "field": "transferOutsideEu", "operator": "eq", "value": true },
                      "outputControlKeys": ["CTRL-CACHE-01"]
                    }
                  ]
                }
                """);

            var engine = new PolicyEngine();
            var facts = new Dictionary<string, object?> { ["transferOutsideEu"] = true };

            var firstRun = engine.Evaluate(rootPath, facts);
            Assert.Single(firstRun);

            WritePolicyFile(rootPath, """
                {
                  "rules": [
                    {
                      "ruleKey": "RUNTIME_CACHE_CHECK",
                      "description": "Cache should invalidate on file content change",
                      "severity": "Medium",
                      "condition": { "field": "transferOutsideEu", "operator": "eq", "value": false },
                      "outputControlKeys": ["CTRL-CACHE-01"]
                    }
                  ]
                }
                """);

            var secondRun = engine.Evaluate(rootPath, facts);
            Assert.Empty(secondRun);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static string CreateTempPolicyDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"normyx-policy-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePolicyFile(string rootPath, string content)
    {
        var path = Path.Combine(rootPath, "pack.json");
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
