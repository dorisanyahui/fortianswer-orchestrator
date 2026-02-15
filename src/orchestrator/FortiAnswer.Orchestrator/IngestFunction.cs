using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using FortiAnswer.Orchestrator.Services;

namespace FortiAnswer.Orchestrator.Functions;

public sealed class IngestFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IngestionOrchestrator _ingestor;

    public IngestFunction(IngestionOrchestrator ingestor)
    {
        _ingestor = ingestor;
    }

    public record IngestRequest(string? Prefix, int? MaxFiles);

    [Function("ingest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken ct)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var input = string.IsNullOrWhiteSpace(body)
            ? new IngestRequest(null, 5)
            : (JsonSerializer.Deserialize<IngestRequest>(body, JsonOptions) ?? new IngestRequest(null, 5));

        var maxFiles = input.MaxFiles.GetValueOrDefault(5);
        if (maxFiles <= 0) maxFiles = 5;

        var (files, chunks) = await _ingestor.RunAsync(input.Prefix, maxFiles, ct);

        var res = req.CreateResponse(HttpStatusCode.OK);
        
        await res.WriteAsJsonAsync(new
        {
            status = "ok",
            filesProcessed = files,
            chunksUploaded = chunks,
            prefix = input.Prefix,
            maxFiles
        }, cancellationToken: ct);


        return res;
    }
}
