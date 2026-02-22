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
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "ingest")] HttpRequestData req,
        CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N");

        try
        {
            // ---- read body (optional) ----
            var bodyText = await new StreamReader(req.Body).ReadToEndAsync();

            IngestRequest input;
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                // Sprint1: default ingest public/
                input = new IngestRequest("public/", 20);
            }
            else
            {
                input = JsonSerializer.Deserialize<IngestRequest>(bodyText, JsonOptions)
                        ?? new IngestRequest("public/", 20);
            }

            // ---- normalize & validate prefix ----
            var normalizedPrefix = NormalizePrefix(input.Prefix);

            // ---- validate maxFiles ----
            var maxFiles = input.MaxFiles.GetValueOrDefault(20);
            if (maxFiles < 1) maxFiles = 20;
            if (maxFiles > 200) maxFiles = 200; // safety cap for demo

            var actionHints = new List<string>
            {
                $"requestId={requestId}",
                $"prefix={normalizedPrefix}",
                $"maxFiles={maxFiles}"
            };

            // ---- run ingest ----
            var startedUtc = DateTimeOffset.UtcNow;
            var (files, chunks) = await _ingestor.RunAsync(normalizedPrefix, maxFiles, ct);
            var elapsedMs = (long)(DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds;

            actionHints.Add($"filesProcessed={files}");
            actionHints.Add($"chunksUploaded={chunks}");
            actionHints.Add($"elapsedMs={elapsedMs}");

            var res = req.CreateResponse(HttpStatusCode.OK);
            // ❗不要手动 Add Content-Type；WriteAsJsonAsync 会自动设置
            res.Headers.Add("x-correlation-id", requestId);

            await res.WriteAsJsonAsync(new
            {
                status = "ok",
                requestId,
                prefix = normalizedPrefix,
                maxFiles,
                filesProcessed = files,
                chunksUploaded = chunks,
                elapsedMs,
                actionHints
            }, cancellationToken: ct);

            return res;
        }
        catch (ArgumentException ex)
        {
            var res = req.CreateResponse(HttpStatusCode.BadRequest);
            res.Headers.Add("x-correlation-id", requestId);

            await res.WriteAsJsonAsync(new
            {
                status = "error",
                requestId,
                error = "ValidationError",
                message = ex.Message
            }, cancellationToken: ct);

            return res;
        }
        catch (Exception ex)
        {
            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            res.Headers.Add("x-correlation-id", requestId);

            await res.WriteAsJsonAsync(new
            {
                status = "error",
                requestId,
                error = "InternalError",
                message = ex.Message
            }, cancellationToken: ct);

            return res;
        }
    }

    private static string NormalizePrefix(string? prefix)
    {
        var p = (prefix ?? "").Trim();

        if (string.IsNullOrWhiteSpace(p))
            return "public/";

        if (p.StartsWith("/")) p = p.TrimStart('/');
        if (!p.EndsWith("/")) p += "/";

        p = p.ToLowerInvariant();

        return p switch
        {
            "public/" => "public/",
            "internal/" => "internal/",
            "confidential/" => "confidential/",
            "restricted/" => "restricted/",
            _ => throw new ArgumentException(
                "Invalid prefix. Allowed values: public/, internal/, confidential/, restricted/. " +
                "Example body: { \"prefix\": \"public/\", \"maxFiles\": 20 }")
        };
    }
}