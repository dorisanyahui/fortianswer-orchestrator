using FortiAnswer.Orchestrator.Services;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// 先不要加 Application Insights（等 ingest 跑通再加）
// builder.Services.AddApplicationInsightsTelemetryWorkerService();
// builder.Services.ConfigureFunctionsApplicationInsights();

// 基础 HttpClientFactory（可留可不留；有 typed client 也能自动带上）
builder.Services.AddHttpClient();

// ------------------------------------------------------------
// Core services (Singleton OK)
// ------------------------------------------------------------
builder.Services.AddSingleton<WebSearchService>();
builder.Services.AddSingleton<RetrievalService>();
builder.Services.AddSingleton<PromptBuilder>();
builder.Services.AddSingleton<DocxTextExtractor>();

// ------------------------------------------------------------
// ✅ HTTP-calling services: use Typed HttpClient (关键)
// ------------------------------------------------------------

builder.Services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>();
builder.Services.AddHttpClient<AzureAiSearchIngestService>();
builder.Services.AddHttpClient<PdfTextExtractor>();



// GroqClient uses IHttpClientFactory -> use Singleton (NOT typed client)
builder.Services.AddSingleton<GroqClient>();

// ------------------------------------------------------------
// ingest services
// ------------------------------------------------------------
builder.Services.AddSingleton<BlobDocumentSource>();
builder.Services.AddSingleton<TextChunker>();
builder.Services.AddSingleton<IngestionOrchestrator>();

// ------------------------------------------------------------
// extractors
// ------------------------------------------------------------
builder.Services.AddSingleton<PdfTextExtractor>();
builder.Services.AddSingleton<DocxTextExtractor>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();

builder.Build().Run();
