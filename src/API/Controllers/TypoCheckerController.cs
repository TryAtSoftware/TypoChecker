using Microsoft.AspNetCore.Mvc;
using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Reflection.Metadata;
using System.Text;
using System.IO;
using API.Settings;
using Microsoft.Extensions.Options;
using API.Features;

namespace API.Controllers
{
    [ApiController, Route("api")]
    public class TypoCheckerController : ControllerBase
    {
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
                using var stream = file.OpenReadStream();
                var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream, new AnalyzeDocumentOptions { Locale = "bg" }, cancellationToken);
                var result = operation.Value;

                foreach (var word in result.Pages.SelectMany(x => x.Words))
                {
                    if (!this._wordsRegister.Contains(word.Content))
                    {
                        incorrectWords.Add(word);
                    }
                }
            }

            return this.Ok(incorrectWords.Select(x => x.Content));
        }
    }
}
