using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// ------------------------------------------------------------
// Base services
// ------------------------------------------------------------
builder.Services.AddHttpClient();

// ------------------------------------------------------------
// Core services (Singleton OK)
// ------------------------------------------------------------
builder.Services.AddSingleton<WebSearchService>();
builder.Services.AddSingleton<RetrievalService>();
builder.Services.AddSingleton<PromptBuilder>();

// GroqClient uses IHttpClientFactory -> singleton is fine
builder.Services.AddSingleton<GroqClient>();

// ------------------------------------------------------------
// ✅ Table Storage (Tickets / ConversationLogs / Feedback)
// Reuse existing BLOB_CONNECTION (same storage account connection string)
// ------------------------------------------------------------
builder.Services.AddSingleton<TableStorageService>(sp =>
{
    var log = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TableStorageService>>();

    var conn = Environment.GetEnvironmentVariable("BLOB_CONNECTION");
    if (string.IsNullOrWhiteSpace(conn))
        throw new InvalidOperationException("Missing BLOB_CONNECTION env var (used for TableStorageService).");

    return new TableStorageService(conn, log);
});

// ------------------------------------------------------------
// HTTP-calling services: Typed HttpClient
// ------------------------------------------------------------
builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddHttpClient<AzureAiSearchIngestService>();

// If your PdfTextExtractor makes HTTP calls, keep it typed.
// If it doesn't, you can register as singleton instead.
// We'll keep typed as you had.
builder.Services.AddHttpClient<PdfTextExtractor>();

// ------------------------------------------------------------
// Ingest services
// ------------------------------------------------------------
builder.Services.AddSingleton<BlobDocumentSource>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<IngestionOrchestrator>();

// ------------------------------------------------------------
// Extractors
// (Remove duplicates; register concrete once + the interface)
// ------------------------------------------------------------
builder.Services.AddSingleton<DocxTextExtractor>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();


builder.Services.AddSingleton<UsersTableService>();

builder.Services.AddSingleton<TicketsTableService>(sp =>
{
    var conn = Environment.GetEnvironmentVariable("BLOB_CONNECTION");
    if (string.IsNullOrWhiteSpace(conn))
        throw new InvalidOperationException("Missing BLOB_CONNECTION env var (used for TicketsTableService).");

    return new TicketsTableService(conn);
});

// NOTE: PdfTextExtractor is registered as typed HttpClient above.
// Do NOT also AddSingleton<PdfTextExtractor>() again, to avoid confusion.

builder.Build().Run();