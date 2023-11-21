using Microsoft.AspNetCore.Mvc;
using System.Text;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Reflection.Metadata;
using System.Text;
using System.IO;
using API.Settings;
using Microsoft.Extensions.Options;

namespace API.Controllers
{
    [ApiController, Route("api")]
    public class TypoCheckerController : ControllerBase
    {
        private readonly DocumentIntelligenceSettings _settings;
        public TypoCheckerController(IOptions<DocumentIntelligenceSettings> options)
        {
            this._settings = options.Value ?? throw new InvalidOperationException("DocumentIntelligenceSettings must be configured!");
        }

        [HttpPost("check")]
        public async Task<IActionResult> CheckAsync()
        {
            var endpoint = "https://typochecker-ocr.cognitiveservices.azure.com/";
            var apiKey = this._settings.APIKey;
            var credential = new AzureKeyCredential(apiKey);
            var client = new DocumentAnalysisClient(new Uri(endpoint), credential);

            foreach (var file in this.Request.Form.Files)
            {
                using var stream = file.OpenReadStream();
                var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", stream, new AnalyzeDocumentOptions { Locale = "bg" });
            }

            return this.Ok();
        }
    }
}
