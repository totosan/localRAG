using Microsoft.KernelMemory.DataFormats;
using localRAG.Plugins;
using Tesseract;

namespace localRAG
{
    public class TesseractOCR : IOcrEngine
    {
        private TesseractEngine _engine;

        public TesseractOCR()
        {
            // Initialize Tesseract engine

            string tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? throw new InvalidOperationException("TESSDATA_PREFIX environment variable is not set.");
            _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);

        }
    
        public Task<string> ExtractTextFromImageAsync(Stream imageContent, CancellationToken cancellationToken = default)
        {
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                imageContent.CopyTo(ms);
                imageBytes = ms.ToArray();
            }
            var content = PdfExtractorizer.ExtractTextFromImage(imageBytes);
            return Task.FromResult(content);
        }

        ~TesseractOCR()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            _engine?.Dispose();
            }
        }
    }
}