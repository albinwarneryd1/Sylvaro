namespace Normyx.Infrastructure.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "data/uploads";
}
