namespace FortiAnswer.Orchestrator.Services;

public sealed class TextChunker
{
    public IEnumerable<string> ChunkByChars(string text, int chunkChars = 1200, int overlapChars = 150)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        int i = 0;
        while (i < text.Length)
        {
            int j = Math.Min(i + chunkChars, text.Length);
            yield return text.Substring(i, j - i);

            if (j >= text.Length) yield break;
            i = Math.Max(0, j - overlapChars);
        }
    }
}
