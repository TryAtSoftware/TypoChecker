namespace API.Controllers;

using System.Text;
using System.Threading;
using API.Features;
using API.Settings;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using iText.IO.Image;
using iText.Kernel.Pdf;
using itext.pdfimage.Extensions;
using iText.Layout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Drawing.Imaging;
using System.Drawing;
using iText.Kernel.Pdf.Canvas;

[ApiController, Route("api")]
public class TypoCheckerController : ControllerBase
{
    private const double MIN_CONFIDENCE = 0.8;

    private static readonly char[] _delimiters = ".,-!?:;'\"()".ToCharArray();
    private static readonly SKPaint _incorrectWordPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke };
    private static readonly SKPaint _unreadableWordPaint = new SKPaint { Color = SKColors.Yellow, Style = SKPaintStyle.Stroke };

    private readonly IWordsRegister _wordsRegister;
    private readonly DocumentAnalysisClient _client;

    public TypoCheckerController(IWordsRegister wordsRegister, IOptions<DocumentIntelligenceSettings> options)
    {
        var settings = options.Value ?? throw new InvalidOperationException("DocumentIntelligenceSettings must be configured!");
        this._wordsRegister = wordsRegister ?? throw new ArgumentNullException(nameof(wordsRegister));

        this._client = new DocumentAnalysisClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.APIKey));
    }

    [HttpPost("check_imgs")]
    public async Task<IActionResult> CheckImagesAsync(CancellationToken cancellationToken)
    {
        var pathToDataDirectory = this.ConstructPathToDataDirectory();
        Directory.CreateDirectory(pathToDataDirectory);

        var pathToResultDirectory = this.ConstructPathToResultDirectory();
        Directory.CreateDirectory(pathToResultDirectory);

        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 5 };
        await Parallel.ForEachAsync(this.Request.Form.Files, parallelOptions, (file, ct) => this.ProcessImageAsync(pathToDataDirectory, pathToResultDirectory, file, ct));

        return this.Ok();
    }

    [HttpPost("check_pdfs")]
    public async Task<IActionResult> CheckPdfsAsync(CancellationToken cancellationToken)
    {
        var pathToDataDirectory = this.ConstructPathToDataDirectory();
        Directory.CreateDirectory(pathToDataDirectory);

        var pathToResultDirectory = this.ConstructPathToResultDirectory();
        Directory.CreateDirectory(pathToResultDirectory);

        var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = 5 };
        await Parallel.ForEachAsync(this.Request.Form.Files, parallelOptions, (file, ct) => this.ProcessPdfAsync(pathToDataDirectory, pathToResultDirectory, file, ct));

        return this.Ok();
    }

    private async ValueTask ProcessPdfAsync(string pathToDataDirectory, string pathToResultDirectory, IFormFile file, CancellationToken cancellationToken)
    {
        var pathToFile = Path.Combine(pathToDataDirectory, file.FileName);
        await SaveFileAsync(pathToFile, file, cancellationToken);

        var analyzationResult = await AnalyzeAsync(pathToFile, this._client, cancellationToken);

        var analyzationResultPerPage = new List<IReadOnlyList<DocumentWord>>();

        foreach(var page in analyzationResult.Pages) {
            analyzationResultPerPage.Add(page.Words);
        }

        using var pdfReader = new PdfReader(pathToFile);
        var pathToWriteableFile = Path.Combine(pathToResultDirectory, file.FileName);
        System.IO.File.Copy(pathToFile, pathToWriteableFile);
        using var pdfWriter = new PdfWriter(pathToWriteableFile);
        using var pdfDocument = new PdfDocument(pdfReader, pdfWriter);

        //TODO: Make parallel async
        for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
        {
            var page = pdfDocument.GetPage(i);
            using var bitmap = ToSKBitmap(page.ConvertPageToBitmap()).Result;
            using var canvas = new SKCanvas(bitmap);
            var pdfCanvas = new PdfCanvas(page);

            //TODO: OrganizePageWords not implemented
            var analyzedWords = OrganizePageWords(analyzationResultPerPage[i]);
            var statusses = new WordStatus[analyzedWords.Length];

            //TODO: Outline wrong words
            this.ClassifyWords(analyzedWords, statusses);
            //OutlineWord()

            using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
            //TODO: SaveEditedPageAsync not implemented
            await SaveEditedPageAsync(pdfCanvas, imageData, cancellationToken);
        }
    }
    public async Task<SKBitmap> ToSKBitmap(Bitmap bitmap)
    {
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);

            stream.Seek(0, SeekOrigin.Begin);

            return SKBitmap.Decode(stream);
        }
    }

    private async ValueTask ProcessImageAsync(string pathToDataDirectory, string pathToResultDirectory, IFormFile file, CancellationToken cancellationToken)
    {
        var pathToFile = Path.Combine(pathToDataDirectory, file.FileName);
        var pathToResult = Path.Combine(pathToResultDirectory, $"{Path.GetFileNameWithoutExtension(file.FileName)}.png");
        await SaveFileAsync(pathToFile, file, cancellationToken);

        var analyzationResult = await AnalyzeAsync(pathToFile, this._client, cancellationToken);

        using var bitmap = await LoadImageForEditAsync(pathToFile, cancellationToken);
        using var canvas = new SKCanvas(bitmap);

        var analyzedWords = OrganizeWords(analyzationResult);
        var statusses = new WordStatus[analyzedWords.Length];

        this.ClassifyWords(analyzedWords, statusses);

        for (var i = 0; i < analyzedWords.Length; i++)
            OutlineWord(analyzedWords[i].Word, statusses[i], canvas);

        using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
        await SaveEditedImageAsync(pathToResult, imageData, cancellationToken);
    }

    private void OutlineWord(DocumentWord word, WordStatus status, SKCanvas canvas)
    {
        SKPaint? paint = status switch
        {
            WordStatus.Unreadable => _unreadableWordPaint,
            WordStatus.Incorrect => _incorrectWordPaint,
            _ => null
        };

        if (paint is null) return;

        for (var j = 0; j < word.BoundingPolygon.Count; j++)
        {
            var nextIndex = (j + 1) % word.BoundingPolygon.Count;
            canvas.DrawLine(word.BoundingPolygon[j].X, word.BoundingPolygon[j].Y, word.BoundingPolygon[nextIndex].X, word.BoundingPolygon[nextIndex].Y, paint);
        }
    }

    private void ClassifyWords(WordWrapper[] analyzedWords, WordStatus[] statusses)
    {
        var index = 0;
        var wordBuilder = new StringBuilder();

        while (index < analyzedWords.Length)
        {
            if (!IsReadable(analyzedWords[index].Word))
            {
                statusses[index] = WordStatus.Unreadable;
                index++;
            }
            else
            {
                int wordSpan = 1, prevLineIndex = analyzedWords[index].LineIndex;
                while (index + wordSpan < analyzedWords.Length
                    && IsReadable(analyzedWords[index + wordSpan].Word)
                    && analyzedWords[index + wordSpan - 1].Word.Content[^1] == '-'
                    && analyzedWords[index + wordSpan].LineIndex > prevLineIndex)
                {
                    prevLineIndex = analyzedWords[index + wordSpan].LineIndex;
                    wordSpan++;
                }

                var isCorrect = this.CheckCorrectness(analyzedWords, index, wordSpan, wordBuilder);
                WordStatus status = isCorrect ? WordStatus.Correct : WordStatus.Incorrect;
                for (var i = 0; i < wordSpan; i++) statusses[index + i] = status;

                index += wordSpan;
            }
        }
    }

    private bool CheckCorrectness(WordWrapper[] analyzedWords, int index, int wordSpan, StringBuilder wordBuilder)
    {
        // This loop will be responsible for placing a dash at a correct place.
        for (var i = 0; i < wordSpan; i++)
        {
            for (var j = 0; j < wordSpan; j++)
            {
                var sanitizedWordContent = SanitizeWord(analyzedWords[index + j].Word.Content);

                wordBuilder.Append(sanitizedWordContent);
                if (j + 1 == i) wordBuilder.Append('-');
            }

            var finalWord = wordBuilder.ToString();
            wordBuilder.Length = 0;

            if (this._wordsRegister.Contains(finalWord)) return true;
        }

        return false;
    }

    private string AccessSanitizedTraceIdentifier() => this.HttpContext.TraceIdentifier.Replace(':', '_');

    private string ConstructPathToDataDirectory() => Path.Combine(AppContext.BaseDirectory, "_storage", this.AccessSanitizedTraceIdentifier(), "data");

    private string ConstructPathToResultDirectory() => Path.Combine(AppContext.BaseDirectory, "_storage", this.AccessSanitizedTraceIdentifier(), "results");

    private static bool IsReadable(DocumentWord word) => word.Confidence >= MIN_CONFIDENCE;

    private static async Task SaveFileAsync(string pathToFile, IFormFile formFile, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(pathToFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await formFile.CopyToAsync(fileStream, cancellationToken);
    }

    private static async Task<AnalyzeResult> AnalyzeAsync(string pathToFile, DocumentAnalysisClient client, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(pathToFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", fileStream, new AnalyzeDocumentOptions { Locale = "bg", Pages = { "1-1999" } }, cancellationToken);
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
    private static async Task SaveEditedPageAsync(PdfCanvas pdfCanvas, SKData imageData, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
        //pdfCanvas.AddImageAt()

        pdfCanvas.Release();
    }
    private static string SanitizeWord(string originalWord) => originalWord.Trim(_delimiters);

    private static WordWrapper[] OrganizeWords(AnalyzeResult analyzeResult)
    {
        var result = new List<WordWrapper>();

        var lineIndex = 0;
        foreach (var page in analyzeResult.Pages)
        {
            foreach (var line in page.Lines)
            {
                foreach (var word in line.GetWords())
                {
                    var wrapper = new WordWrapper { LineIndex = lineIndex, Word = word };
                    result.Add(wrapper);
                }

                lineIndex++;
            }
        }

        return result.ToArray();
    }

    private static WordWrapper[] OrganizePageWords(IReadOnlyList<DocumentWord> page)
    {
        var result = new List<WordWrapper>();

        throw new NotImplementedException();

        return result.ToArray();
    }
    private sealed record WordWrapper
    {
        public required int LineIndex { get; init; }
        public required DocumentWord Word { get; init; }
    }

    private enum WordStatus
    {
        Correct,
        Unreadable,
        Incorrect
    }
}