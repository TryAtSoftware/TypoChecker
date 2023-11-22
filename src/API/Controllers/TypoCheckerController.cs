using API.Features;
using API.Settings;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace API.Controllers
{
    [ApiController, Route("api")]
    public class TypoCheckerController : ControllerBase
    {
        private static readonly char[] _delimiters = ".,!?:'\"()".ToCharArray();

        private readonly DocumentIntelligenceSettings _settings;
        private readonly IWordsRegister _wordsRegister;
        public TypoCheckerController(IWordsRegister wordsRegister, IOptions<DocumentIntelligenceSettings> options)
        {
            this._settings = options.Value ?? throw new InvalidOperationException("DocumentIntelligenceSettings must be configured!");
            this._wordsRegister = wordsRegister ?? throw new ArgumentNullException(nameof(wordsRegister));
        }

        [HttpPost("check")]
        public async Task<IActionResult> CheckAsync(CancellationToken cancellationToken)
        {
            var endpoint = "https://typochecker-ocr.cognitiveservices.azure.com/";
            var apiKey = this._settings.APIKey;
            var credential = new AzureKeyCredential(apiKey);
            var client = new DocumentAnalysisClient(new Uri(endpoint), credential);
            List<DocumentWord> incorrectWords = new List<DocumentWord>();

            foreach (var file in this.Request.Form.Files)
            {
                var pathToFile = await SaveImageAsync("data", this.HttpContext.TraceIdentifier, file, cancellationToken);

                await using var fileStream = new FileStream(pathToFile, FileMode.Open, FileAccess.Read, FileShare.None);
                var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", fileStream, new AnalyzeDocumentOptions { Locale = "bg" }, cancellationToken);
                var result = operation.Value;

                fileStream.Seek(0, SeekOrigin.Begin);
                var image = SKImage.FromEncodedData(fileStream);

                // TODO: Drawe a rectangle around all incorrect words.

                foreach (var word in result.Pages.SelectMany(x => x.Words))
                {
                    var sanitizedContent = SanitizeWord(word.Content);
                    if (!this._wordsRegister.Contains(sanitizedContent)) incorrectWords.Add(word);
                }
            }

            return this.Ok(incorrectWords.Select(x => x.Content));
        }

        private async Task<string> SaveImageAsync(string dataDirectory, string traceIdentifier, IFormFile formFile, CancellationToken cancellationToken)
        {
            var pathToFolder = Path.Combine(AppContext.BaseDirectory, dataDirectory, traceIdentifier.Replace(':', '_'));
            if (!Directory.Exists(pathToFolder)) Directory.CreateDirectory(pathToFolder);

            var fullPath = Path.Combine(pathToFolder, formFile.FileName);
            await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            await formFile.CopyToAsync(fileStream, cancellationToken);

            return fullPath;
        }

        private string SanitizeWord(string originalWord) => originalWord.Trim(_delimiters);
    }
}
