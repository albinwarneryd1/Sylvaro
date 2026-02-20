using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sylvaro.Application.Abstractions;

namespace Sylvaro.Infrastructure.Exports;

public class PdfExportService : IExportService
{
    public Task<byte[]> GeneratePdfAsync(string title, IReadOnlyCollection<string> lines, CancellationToken cancellationToken = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var bronze = Color.FromHex("#5C3A21");
        var darkBrown = Color.FromHex("#3A2414");
        var lightStone = Color.FromHex("#ECE8E2");
        var charcoal = Color.FromHex("#1F1C18");

        var bytes = Document.Create(container =>
            container.Page(page =>
            {
                page.Margin(24);
                page.PageColor(Colors.White);

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(brand =>
                        {
                            brand.Item().Text("SYLVARO")
                                .FontFamily("Times New Roman")
                                .SemiBold()
                                .FontSize(22)
                                .FontColor(bronze)
                                .LetterSpacing(1.2f);

                            brand.Item().Text("Regulatory Intelligence Infrastructure")
                                .FontSize(9)
                                .FontColor(darkBrown);
                        });
                    });

                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(lightStone);
                    col.Item().PaddingTop(8).Text(title)
                        .FontSize(15)
                        .SemiBold()
                        .FontColor(darkBrown);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            col.Item().PaddingBottom(4).Text(" ").FontSize(2);
                            continue;
                        }

                        if (line.StartsWith("# ", StringComparison.Ordinal))
                        {
                            col.Item().PaddingTop(4).PaddingBottom(4).Text(line[2..])
                                .FontSize(14)
                                .SemiBold()
                                .FontColor(darkBrown);
                            continue;
                        }

                        if (line.StartsWith("## ", StringComparison.Ordinal))
                        {
                            col.Item().PaddingTop(3).PaddingBottom(2).Text(line[3..])
                                .FontSize(11)
                                .SemiBold()
                                .FontColor(bronze);
                            continue;
                        }

                        if (line.StartsWith("| ", StringComparison.Ordinal))
                        {
                            col.Item().Background(Color.FromHex("#F5F2ED")).Border(1).BorderColor(lightStone).Padding(4).Text(line)
                                .FontSize(9)
                                .FontFamily("Courier New")
                                .FontColor(charcoal);
                            continue;
                        }

                        if (line.StartsWith("- ", StringComparison.Ordinal))
                        {
                            col.Item().Row(row =>
                            {
                                row.ConstantItem(12).Text("•").FontSize(10).FontColor(bronze);
                                row.RelativeItem().Text(line[2..]).FontSize(10).FontColor(charcoal);
                            });
                            continue;
                        }

                        col.Item().PaddingBottom(2).Text(line).FontSize(10).FontColor(charcoal);
                    }
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1).LineColor(lightStone);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("SYLVARO Governance Export").FontSize(9).FontColor(darkBrown);
                        row.RelativeItem().AlignRight().Text($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC").FontSize(9).FontColor(darkBrown);
                    });
                });
            }))
            .GeneratePdf();

        return Task.FromResult(bytes);
    }
}
