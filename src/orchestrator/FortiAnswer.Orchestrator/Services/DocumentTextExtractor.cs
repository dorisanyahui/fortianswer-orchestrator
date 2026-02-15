using System.Text;

namespace FortiAnswer.Orchestrator.Services;

public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private readonly PdfTextExtractor _pdf;
    private readonly DocxTextExtractor _docx;

    public DocumentTextExtractor(PdfTextExtractor pdf, DocxTextExtractor docx)
    {
        _pdf = pdf;
        _docx = docx;
    }

    public async Task<string> ExtractTextAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".pdf"  => await _pdf.ExtractAsync(content, ct),
            ".docx" => await _docx.ExtractAsync(content, ct),
            ".txt" or ".md" => await ReadAllTextAsync(content, ct),
            _ => throw new InvalidOperationException($"Unsupported file type: {ext} ({fileName})")
        };
    }

    private static async Task<string> ReadAllTextAsync(Stream s, CancellationToken ct)
    {
        using var reader = new StreamReader(s, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }
}
