using iText.Layout.Element;
using localRAG.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.KernelMemory.Pipeline;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using SixLabors.ImageSharp;


public class CustomPdfDecoder : IContentDecoder
{
    private readonly ILogger<CustomPdfDecoder> _log;

    public CustomPdfDecoder(ILoggerFactory? loggerFactory = null)
    {
        this._log = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<CustomPdfDecoder>();
    }

    /// <inheritdoc />
    public bool SupportsMimeType(string mimeType)
    {
        return mimeType != null && mimeType.StartsWith(MimeTypes.Pdf, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(string filename, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filename);
        return this.DecodeAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(BinaryData data, CancellationToken cancellationToken = default)
    {
        using var stream = data.ToStream();
        return this.DecodeAsync(stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FileContent> DecodeAsync(Stream data, CancellationToken cancellationToken = default)
    {
        this._log.LogDebug("Extracting text from PDF file");

        var result = new FileContent(MimeTypes.PlainText);

        using PdfDocument? pdfDocument = PdfDocument.Open(data);
        if (pdfDocument == null) { return Task.FromResult(result); }

        var options = new ContentOrderTextExtractor.Options
        {
            ReplaceWhitespaceWithSpace = true,
            SeparateParagraphsWithDoubleNewline = false,
        };

        foreach (Page? page in pdfDocument.GetPages().Where(x => x != null))
        {
            string pageContent = (ContentOrderTextExtractor.GetText(page, options) ?? string.Empty).ReplaceLineEndings(" ");
            if (pageContent.IsNullOrEmpty())
            {
                var image = page.GetImages().FirstOrDefault();
                if (image != null)
                {
                    // convert the image to an inmemory PNG
                    byte[] imageBytes = image.RawBytes.ToArray();
                    byte[] imageBytesPng;
                    image.TryGetPng(out imageBytesPng);
                    //var png = SixLabors.ImageSharp.Image.Load(new MemoryStream(imageBytes));
                    pageContent = PdfExtractorizer.ExtractTextFromImage(imageBytesPng == null ? imageBytes: imageBytesPng);
                }
            }
            result.Sections.Add(new FileSection(page.Number, pageContent, false));
        }

        return Task.FromResult(result);
    }
}