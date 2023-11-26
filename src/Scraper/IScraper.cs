namespace Scraper;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;

public interface IScraper
{
    Task ReadMainWords(IWebDriver driver, Stream resultStream, CancellationToken cancellationToken = default);
    Task ReadWordForms(IWebDriver driver, string word, Stream resultStream, CancellationToken cancellationToken = default);
}