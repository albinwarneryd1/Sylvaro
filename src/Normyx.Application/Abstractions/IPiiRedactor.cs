namespace Normyx.Application.Abstractions;

public interface IPiiRedactor
{
    string Redact(string input);
}
