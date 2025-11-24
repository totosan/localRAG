using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Diagnostics;
using localRAG;

public class CustomPdfDecoder : IContentDecoder
{
    private readonly ILogger<CustomPdfDecoder> _log;
    private readonly HttpClient _httpClient;
    private readonly Uri _doclingEndpoint;
    private readonly string? _apiKey;
    private const string PdfMimeType = "application/pdf";
    private const string PlainTextMimeType = "text/plain";

    // German and English stopwords for document normalization
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "der", "die", "das", "und", "oder", "aber", "in", "auf", "von", "zu", "mit", "für",
        "ist", "sind", "war", "waren", "wird", "werden", "wurde", "wurden", "hat", "haben",
        "ein", "eine", "einer", "einem", "einen", "des", "dem", "den", "als", "auch", "an",
        "bei", "nach", "um", "am", "im", "zum", "zur", "über", "unter", "durch", "vor",
        "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "is", "are",
        "was", "were", "be", "been", "have", "has", "had", "do", "does", "did", "this", "that"
    };

    public CustomPdfDecoder(ILoggerFactory? loggerFactory = null, IHttpClientFactory? httpClientFactory = null)
    {
        this._log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<CustomPdfDecoder>();
        this._httpClient = httpClientFactory?.CreateClient(nameof(CustomPdfDecoder)) ?? new HttpClient();

        var baseUrl = Helpers.EnvVarOrDefault("DOCLING_ENDPOINT", "http://localhost:5001");
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        this._doclingEndpoint = new Uri(new Uri(baseUrl), "v1/convert/file");
        this._apiKey = Environment.GetEnvironmentVariable("DOCLING_API_KEY");
    }

    /// <inheritdoc />
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null && mimeType.StartsWith(PdfMimeType, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filename);
        return await this.DecodeInternalAsync(stream, Path.GetFileName(filename), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        await using var stream = data.ToStream();
        return await this.DecodeInternalAsync(stream, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        return this.DecodeInternalAsync(data, null, cancellationToken);
    }

    private async Task<FileContent> DecodeInternalAsync(Stream data, string? fileName, CancellationToken cancellationToken)
    {
        var result = new FileContent(PlainTextMimeType);
        var payloadStream = new MemoryStream();
        await data.CopyToAsync(payloadStream, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, this._doclingEndpoint);
        using var formContent = this.CreateMultipartContent(payloadStream, fileName);
        request.Content = formContent;

        if (!string.IsNullOrWhiteSpace(this._apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", this._apiKey);
        }

        this._log.LogDebug("Sending PDF to Docling service at {Endpoint}", this._doclingEndpoint);

        try
        {
            using var response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                this._log.LogError("Docling conversion failed: {StatusCode} - {Error}", response.StatusCode, errorPayload);
                return result;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var jsonDoc = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!jsonDoc.RootElement.TryGetProperty("document", out var documentElement))
            {
                this._log.LogWarning("Docling response missing document payload");
                return result;
            }

            if (this.TryAddJsonSections(documentElement, result))
            {
                return result;
            }

            if (this.TryAddPlainText(documentElement, result))
            {
                return result;
            }

            this._log.LogWarning("Docling response did not include usable text content");
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
        {
            this._log.LogError(ex, "Docling conversion failed");
            return result;
        }
    }

    private MultipartFormDataContent CreateMultipartContent(MemoryStream payloadStream, string? fileName)
    {
        payloadStream.Position = 0;
        var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(payloadStream);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(PdfMimeType);
        form.Add(streamContent, "files", string.IsNullOrWhiteSpace(fileName) ? "document.pdf" : fileName);

        form.Add(new StringContent("pdf"), "from_formats");
        form.Add(new StringContent("json"), "to_formats");
        form.Add(new StringContent("text"), "to_formats");
        form.Add(new StringContent("true"), "do_ocr");
        form.Add(new StringContent("false"), "force_ocr");

        return form;
    }

    private bool TryAddJsonSections(JsonElement documentElement, FileContent result)
    {
        if (!documentElement.TryGetProperty("json_content", out var jsonContent) || jsonContent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!jsonContent.TryGetProperty("texts", out var textsElement) || textsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var sectionsFound = false;
        var pages = new SortedDictionary<int, StringBuilder>();

        foreach (var textNode in textsElement.EnumerateArray())
        {
            if (!textNode.TryGetProperty("text", out var textValue) || textValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = textValue.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var pageNumber = this.ResolvePageNumber(textNode);
            if (!pages.TryGetValue(pageNumber, out var builder))
            {
                builder = new StringBuilder();
                pages[pageNumber] = builder;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            // Apply normalization pipeline before storing
            var normalizedText = NormalizeText(text);
            if (!string.IsNullOrWhiteSpace(normalizedText))
            {
                builder.Append(normalizedText);
            }
        }

        foreach (var kvp in pages)
        {
            var content = kvp.Value.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var meta = Chunk.Meta(true, kvp.Key);
            var chunk = new Chunk(content, kvp.Key, meta);
            result.Sections.Add(chunk);
            sectionsFound = true;
        }

        return sectionsFound;
    }

    private bool TryAddPlainText(JsonElement documentElement, FileContent result)
    {
        if (!documentElement.TryGetProperty("text_content", out var textElement) || textElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = textElement.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Apply normalization pipeline
        var normalizedText = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        var meta = Chunk.Meta(true, 1);
        var chunk = new Chunk(normalizedText, 1, meta);
        result.Sections.Add(chunk);
        return true;
    }

    private int ResolvePageNumber(JsonElement textNode)
    {
        if (textNode.TryGetProperty("prov", out var provArray) && provArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var prov in provArray.EnumerateArray())
            {
                if (prov.TryGetProperty("page_no", out var pageNoElement) && pageNoElement.TryGetInt32(out var pageNo) && pageNo > 0)
                {
                    return pageNo;
                }
            }
        }

        return 1;
    }

    /// <summary>
    /// Normalizes text using the standard RAG preprocessing pipeline:
    /// 1. Convert to lowercase
    /// 2. Remove punctuation (keep word boundaries)
    /// 3. Remove stopwords
    /// 4. Remove extra whitespace
    /// 5. Trim final result
    /// </summary>
    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Step 1: Convert to lowercase
        var normalized = text.ToLowerInvariant();

        // Step 2: Remove punctuation but keep word boundaries
        // Replace punctuation with spaces to preserve word separation
        normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

        // Step 3: Remove stopwords
        var words = Regex.Split(normalized, @"\s+")
            .Where(w => w.Length > 2 && !Stopwords.Contains(w))
            .ToList();

        // Step 4 & 5: Join words with single space and trim
        normalized = string.Join(" ", words).Trim();

        return normalized;
    }
}