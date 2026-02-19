using System.Text.RegularExpressions;
using Normyx.Application.Abstractions;

namespace Normyx.Infrastructure.AI;

public partial class RegexPiiRedactor : IPiiRedactor
{
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{6,}\b", RegexOptions.Compiled)]
    private static partial Regex LongDigitRegex();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    public string Redact(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var redacted = EmailRegex().Replace(input, "[EMAIL_REDACTED]");
        redacted = LongDigitRegex().Replace(redacted, "[ID_REDACTED]");
        redacted = DateRegex().Replace(redacted, "[DATE_REDACTED]");

        return redacted;
    }
}
