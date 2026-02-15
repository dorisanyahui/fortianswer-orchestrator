using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FortiAnswer.Orchestrator.Services;

public sealed class PdfTextExtractor
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _apiKey;

    // Use latest stable you already tested
    private const string ApiVersion = "2024-11-30";

    public PdfTextExtractor(HttpClient http, IConfiguration cfg)
    {
        _http = http;

        _endpoint = (cfg["DOCINTEL_ENDPOINT"] ?? "").Trim();
        _apiKey = cfg["DOCINTEL_API_KEY"] ?? throw new InvalidOperationException("DOCINTEL_API_KEY missing");

        if (string.IsNullOrWhiteSpace(_endpoint))
            throw new InvalidOperationException("DOCINTEL_ENDPOINT missing");

        if (!_endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DOCINTEL_ENDPOINT must be a URL");

        if (_endpoint.EndsWith("/")) _endpoint = _endpoint.TrimEnd('/');
    }

    public async Task<string> ExtractAsync(Stream pdfStream, CancellationToken ct = default)
    {
        // 1) POST analyze request (prebuilt-read)
        var analyzeUrl = $"{_endpoint}/documentintelligence/documentModels/prebuilt-read:analyze?api-version={ApiVersion}";

        using var content = new StreamContent(pdfStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var req = new HttpRequestMessage(HttpMethod.Post, analyzeUrl);
        req.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        req.Content = content;

        using var res = await _http.SendAsync(req, ct);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"DocIntel analyze failed: {(int)res.StatusCode} {body}");
        }

        // Get Operation-Location for polling
        if (!res.Headers.TryGetValues("operation-location", out var values))
            throw new InvalidOperationException("DocIntel response missing operation-location header.");

        var opUrl = values.First();

        // 2) Poll result until succeeded
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            using var pollReq = new HttpRequestMessage(HttpMethod.Get, opUrl);
            pollReq.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

            using var pollRes = await _http.SendAsync(pollReq, ct);
            var json = await pollRes.Content.ReadAsStringAsync(ct);

            if (!pollRes.IsSuccessStatusCode)
                throw new InvalidOperationException($"DocIntel poll failed: {(int)pollRes.StatusCode} {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.GetProperty("status").GetString() ?? "";
            if (status.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
            {
                // Extract content
                if (root.TryGetProperty("analyzeResult", out var ar) &&
                    ar.TryGetProperty("content", out var contentProp))
                {
                    return (contentProp.GetString() ?? "").Trim();
                }

                return string.Empty;
            }

            if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"DocIntel analyze status=failed: {json}");
        }

        throw new InvalidOperationException("DocIntel analyze timed out after polling.");
    }
}
