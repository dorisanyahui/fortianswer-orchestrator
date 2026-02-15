using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Text;

namespace FortiAnswer.Orchestrator.Services;

public sealed class DocxTextExtractor
{
    public Task<string> ExtractAsync(Stream docxStream, CancellationToken ct = default)
    {
        // OpenXml needs seekable stream; copy if necessary
        Stream seekable = docxStream;
        if (!docxStream.CanSeek)
        {
            var ms = new MemoryStream();
            docxStream.CopyTo(ms);
            ms.Position = 0;
            seekable = ms;
        }
        else
        {
            docxStream.Position = 0;
        }

        using var wordDoc = WordprocessingDocument.Open(seekable, false);
        var body = wordDoc.MainDocumentPart?.Document?.Body;

        if (body is null) return Task.FromResult(string.Empty);

        var sb = new StringBuilder();

        foreach (var para in body.Descendants<Paragraph>())
        {
            var text = para.InnerText?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
        }

        return Task.FromResult(sb.ToString());
    }
}
