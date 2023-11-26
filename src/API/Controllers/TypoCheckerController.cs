namespace API.Controllers;

using System.IO.Compression;
using System.Net.Mime;
using System.Text;
using API.Features;
using API.Settings;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkiaSharp;
using PdfColor = iText.Kernel.Colors.Color;
using SystemColor = System.Drawing.Color;

[ApiController, Route("api")]
public class TypoCheckerController : ControllerBase
{
    private const string STORAGE_DIRECTORY_NAME = "_storage";
    private const string DATA_DIRECTORY_NAME = "data";
    private const string RESULTS_DIRECTORY_NAME = "results";
    private const double MIN_CONFIDENCE = 0.8;
    private const int INCH_TO_POINTS = 72;

    private static readonly char[] _delimiters = ".,-!?:;'\"„“()".ToCharArray();

    private static readonly PdfColor _incorrectWordPdfColor = new DeviceRgb(SystemColor.Red);
    private static readonly PdfColor _unreadableWordPdfColor = new DeviceRgb(SystemColor.Orange);

    private static readonly SKPaint _incorrectWordImgPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke };
    private static readonly SKPaint _unreadableWordImgPaint = new SKPaint { Color = SKColors.Orange, Style = SKPaintStyle.Stroke };

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

        return await this.FinalizeRequestAsync(pathToResultDirectory);
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

        return await this.FinalizeRequestAsync(pathToResultDirectory);
    }

    private async ValueTask ProcessPdfAsync(string pathToDataDirectory, string pathToResultDirectory, IFormFile file, CancellationToken cancellationToken)
    {
        var pathToFile = Path.Combine(pathToDataDirectory, file.FileName);
        var pathToResult = Path.Combine(pathToResultDirectory, file.FileName);
        await SaveFileAsync(pathToFile, file, cancellationToken);

        var analyzationResult = await AnalyzeAsync(pathToFile, this._client, cancellationToken);

        using var pdfReader = new PdfReader(pathToFile);
        await using var pdfWriter = new PdfWriter(pathToResult);
        using var pdfDocument = new PdfDocument(pdfReader, pdfWriter);

        var numberOfPages = pdfDocument.GetNumberOfPages();
        var pageHeights = new float[numberOfPages];
        var canvases = new PdfCanvas[numberOfPages];
        for (var i = 0; i < numberOfPages; i++)
        {
            var page = pdfDocument.GetPage(i + 1);
            pageHeights[i] = page.GetPageSize().GetHeight();
            canvases[i] = new PdfCanvas(page);
        }

        var analyzedWords = OrganizeWords(analyzationResult);
        var statuses = new WordStatus[analyzedWords.Length];

        this.ClassifyWords(analyzedWords, statuses);

        for (var i = 0; i < analyzedWords.Length; i++) OutlineWord(analyzedWords[i].Word, statuses[i], canvases[analyzedWords[i].PageIndex], pageHeights[analyzedWords[i].PageIndex]);
        pdfDocument.Close();
    }

    private async ValueTask ProcessImageAsync(string pathToDataDirectory, string pathToResultDirectory, IFormFile file, CancellationToken cancellationToken)
    {
        var pathToFile = Path.Combine(pathToDataDirectory, file.FileName);
        var pathToResult = Path.Combine(pathToResultDirectory, Path.ChangeExtension(file.FileName, "png"));
        await SaveFileAsync(pathToFile, file, cancellationToken);

        var analyzationResult = await AnalyzeAsync(pathToFile, this._client, cancellationToken);

        using var bitmap = await LoadImageForEditAsync(pathToFile, cancellationToken);
        using var canvas = new SKCanvas(bitmap);

        var analyzedWords = OrganizeWords(analyzationResult);
        var statuses = new WordStatus[analyzedWords.Length];

        this.ClassifyWords(analyzedWords, statuses);

        for (var i = 0; i < analyzedWords.Length; i++) OutlineWord(analyzedWords[i].Word, statuses[i], canvas);

        using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, quality: 100);
        await SaveEditedImageAsync(pathToResult, imageData, cancellationToken);
    }

    private static void OutlineWord(DocumentWord word, WordStatus status, PdfCanvas canvas, float height)
    {
        var color = status switch
        {
            WordStatus.Unreadable => _unreadableWordPdfColor,
            WordStatus.Incorrect => _incorrectWordPdfColor,
            _ => null
        };

        if (color is null) return;

        for (var i = 0; i < word.BoundingPolygon.Count; i++)
        {
            var nextIndex = (i + 1) % word.BoundingPolygon.Count;

            canvas.SetStrokeColor(color);

            // iText7 uses points (pt). However, the Document Intelligence service returns coordinates in inches (for PDF documents). That's why we need to convert all values.
            // iText7 uses the bottom-left corner as a reference point. However, the Document Intelligence service uses the top-left corner as a reference point. That's why we need to "flip" the Y coordinates.
            canvas.MoveTo(word.BoundingPolygon[i].X * INCH_TO_POINTS, height - word.BoundingPolygon[i].Y * INCH_TO_POINTS);
            canvas.LineTo(word.BoundingPolygon[nextIndex].X * INCH_TO_POINTS, height - word.BoundingPolygon[nextIndex].Y * INCH_TO_POINTS);
            canvas.Stroke();
        }
    }

    private static void OutlineWord(DocumentWord word, WordStatus status, SKCanvas canvas)
    {
        var paint = status switch
        {
            WordStatus.Unreadable => _unreadableWordImgPaint,
            WordStatus.Incorrect => _incorrectWordImgPaint,
            _ => null
        };

        if (paint is null) return;

        for (var i = 0; i < word.BoundingPolygon.Count; i++)
        {
            var nextIndex = (i + 1) % word.BoundingPolygon.Count;
            canvas.DrawLine(word.BoundingPolygon[i].X, word.BoundingPolygon[i].Y, word.BoundingPolygon[nextIndex].X, word.BoundingPolygon[nextIndex].Y, paint);
        }
    }

    private void ClassifyWords(WordWrapper[] analyzedWords, WordStatus[] statuses)
    {
        var index = 0;
        var wordBuilder = new StringBuilder();

        while (index < analyzedWords.Length)
        {
            if (!IsReadable(analyzedWords[index].Word))
            {
                statuses[index] = WordStatus.Unreadable;
                index++;
            }
            else
            {
                int wordSpan = 1, prevLineIndex = analyzedWords[index].LineIndex;
                while (index + wordSpan < analyzedWords.Length && IsReadable(analyzedWords[index + wordSpan].Word) && analyzedWords[index + wordSpan - 1].Word.Content[^1] == '-' && analyzedWords[index + wordSpan].LineIndex > prevLineIndex)
                {
                    prevLineIndex = analyzedWords[index + wordSpan].LineIndex;
                    wordSpan++;
                }

                var isCorrect = this.CheckCorrectness(analyzedWords, index, wordSpan, wordBuilder);
                var status = isCorrect ? WordStatus.Correct : WordStatus.Incorrect;
                for (var i = 0; i < wordSpan; i++) statuses[index + i] = status;

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

    private string ConstructPathToDataDirectory() => Path.Combine(AppContext.BaseDirectory, STORAGE_DIRECTORY_NAME, this.AccessSanitizedTraceIdentifier(), DATA_DIRECTORY_NAME);

    private string ConstructPathToResultDirectory() => Path.Combine(AppContext.BaseDirectory, STORAGE_DIRECTORY_NAME, this.AccessSanitizedTraceIdentifier(), RESULTS_DIRECTORY_NAME);

    private async Task<IActionResult> FinalizeRequestAsync(string pathToResultDirectory)
    {
        var pathToZip = Path.ChangeExtension(pathToResultDirectory, "zip");

        await using var fileStream = new FileStream(pathToZip, FileMode.Create, FileAccess.Write, FileShare.None);
        ZipFile.CreateFromDirectory(pathToResultDirectory, fileStream);

        return this.PhysicalFile(pathToZip, MediaTypeNames.Application.Zip, fileDownloadName: Path.GetFileName(pathToZip));
    }

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

    private static string SanitizeWord(string originalWord) => originalWord.Trim(_delimiters);

    private static WordWrapper[] OrganizeWords(AnalyzeResult analyzeResult)
    {
        var result = new List<WordWrapper>();

        var pageIndex = 0;
        var lineIndex = 0;
        foreach (var page in analyzeResult.Pages)
        {
            foreach (var line in page.Lines)
            {
                foreach (var word in line.GetWords())
                {
                    var wrapper = new WordWrapper { PageIndex = pageIndex, LineIndex = lineIndex, Word = word };
                    result.Add(wrapper);
                }

                lineIndex++;
            }

            pageIndex++;
        }

        return result.ToArray();
    }

    private sealed record WordWrapper
    {
        public required int PageIndex { get; init; }
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