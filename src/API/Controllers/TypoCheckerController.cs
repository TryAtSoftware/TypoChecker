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
            var incorrectWords = new List<string>();

            var pathToDataDirectory = this.ConstructPathToDataDirectory();
            Directory.CreateDirectory(pathToDataDirectory);
            
            var pathToResultDirectory = this.ConstructPathToResultDirectory();
            Directory.CreateDirectory(pathToResultDirectory);

            foreach (var file in this.Request.Form.Files)
            {
                var pathToFile = Path.Combine(pathToDataDirectory, file.FileName);
                var pathToResult = Path.Combine(pathToResultDirectory, $"{Path.GetFileNameWithoutExtension(file.FileName)}.png");
                await SaveImageAsync(pathToFile, file, cancellationToken);

                var analyzationResult = await AnalyzeAsync(pathToFile, client, cancellationToken);

                using var bitmap = await LoadImageForEditAsync(pathToFile, cancellationToken);
                using var canvas = new SKCanvas(bitmap);
                var paint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke };

                foreach (var word in analyzationResult.Pages.SelectMany(x => x.Words))
                {
                    var sanitizedContent = SanitizeWord(word.Content);
                    if (!this._wordsRegister.Contains(sanitizedContent))
                    {
                        incorrectWords.Add(sanitizedContent);
                        for (var i = 0; i < word.BoundingPolygon.Count; i++)
                        {
                            var nextIndex = (i + 1) % word.BoundingPolygon.Count;
                            canvas.DrawLine(word.BoundingPolygon[i].X, word.BoundingPolygon[i].Y, word.BoundingPolygon[nextIndex].X, word.BoundingPolygon[nextIndex].Y, paint);
                        }
                    }
                }

                using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
                await SaveEditedImageAsync(pathToResult, imageData, cancellationToken);
            }

            return this.Ok(incorrectWords);
        }

        private string AccessSanitizedTraceIdentifier() => this.HttpContext.TraceIdentifier.Replace(':', '_');
        
        private string ConstructPathToDataDirectory() => Path.Combine(AppContext.BaseDirectory, "_storage", this.AccessSanitizedTraceIdentifier(), "data");

        private string ConstructPathToResultDirectory() => Path.Combine(AppContext.BaseDirectory, "_storage", this.AccessSanitizedTraceIdentifier(), "results");

        private static async Task SaveImageAsync(string pathToFile, IFormFile formFile, CancellationToken cancellationToken)
        {
            await using var fileStream = new FileStream(pathToFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await formFile.CopyToAsync(fileStream, cancellationToken);
        }

        private static async Task<AnalyzeResult> AnalyzeAsync(string pathToFile, DocumentAnalysisClient client, CancellationToken cancellationToken)
        {
            await using var fileStream = new FileStream(pathToFile, FileMode.Open, FileAccess.Read, FileShare.None);
            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", fileStream, new AnalyzeDocumentOptions { Locale = "bg" }, cancellationToken);
            return operation.Value;
        }

        private static async Task<SKBitmap> LoadImageForEditAsync(string pathToFile, CancellationToken cancellationToken)
        {
            await using var fileStream = new FileStream(pathToFile, FileMode.Open, FileAccess.Read, FileShare.None);
            await using var memoryStream = new MemoryStream();

            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            return SKBitmap.Decode(memoryStream.ToArray());
        }

        private static async Task SaveEditedImageAsync(string pathToFile, SKData imageData, CancellationToken cancellationToken)
        {
            await using var fileStream = new FileStream(pathToFile, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var imageDataStream = imageData.AsStream(streamDisposesData: false);
            await imageDataStream.CopyToAsync(fileStream, cancellationToken);
        } 

        private static string SanitizeWord(string originalWord) => originalWord.Trim(_delimiters);
    }
}