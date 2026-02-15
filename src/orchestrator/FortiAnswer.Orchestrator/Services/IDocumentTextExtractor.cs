namespace FortiAnswer.Orchestrator.Services;

public interface IDocumentTextExtractor
{
    /// <summary>
    /// Return extracted plain text from the blob content stream, based on file extension.
    /// ext example: ".pdf" ".docx" ".txt" ".md"
    /// </summary>
    Task<string> ExtractTextAsync(string fileName, Stream content, CancellationToken ct = default);
}
