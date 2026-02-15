namespace FortiAnswer.Orchestrator.Services;

public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string input, CancellationToken ct = default);

    Task<List<float[]>> EmbedBatchAsync(List<string> inputs, CancellationToken ct = default);
}
