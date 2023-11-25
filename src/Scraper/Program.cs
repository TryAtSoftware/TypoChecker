namespace Scraper;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

public static class Program
{
    private const int BUFFER_SIZE = 1024;

    private const string MAIN_WORDS_LIST = "main-words-list.txt";
    private const string FORMS_WORDS_LIST = "forms-words-list.txt";

    public static async Task Main()
    {
        // var scraper = new WiktionaryScraper();
        var scraper = new SlovoredScraper();

        await ReadMainWordsAsync(scraper);
        await FormatWordsList(MAIN_WORDS_LIST);

        await ReadWordFormsAsync(scraper, workers: Math.Min(10, Environment.ProcessorCount));
        await FormatWordsList(FORMS_WORDS_LIST);
    }

    private static async Task ReadMainWordsAsync(IScraper scraper)
    {
        await using var fileStream = new FileStream(MAIN_WORDS_LIST, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: BUFFER_SIZE, useAsync: true);

        using var driver = InstantiateDriver();

        await scraper.ReadMainWords(driver, fileStream);
    }

    private static async Task ReadWordFormsAsync(IScraper scraper, int workers)
    {
        var drivers = new IWebDriver[workers];
        for (var i = 0; i < workers; i++) drivers[i] = InstantiateDriver();

        var availableDrivers = new ConcurrentQueue<IWebDriver>(drivers);

        try
        {
            await using var fileStream = new FileStream(FORMS_WORDS_LIST, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: BUFFER_SIZE, useAsync: true);

            var mainWords = await File.ReadAllLinesAsync(MAIN_WORDS_LIST);
            await Parallel.ForEachAsync(mainWords, new ParallelOptions { MaxDegreeOfParallelism = workers }, (word, _) => ScrapeWordFormsAsync(scraper, availableDrivers, word, fileStream));
        }
        finally
        {
            foreach (var driver in drivers) driver.Dispose();
        }
    }

    private static async ValueTask ScrapeWordFormsAsync(IScraper scraper, ConcurrentQueue<IWebDriver> availableDrivers, string word, Stream stream)
    {
        if (!availableDrivers.TryDequeue(out var driver)) return;

        try
        {
            await scraper.ReadWordForms(driver, word, stream);
        }
        finally
        {
            availableDrivers.Enqueue(driver);
        }
    }

    private static async Task FormatWordsList(string fileName)
    {
        var allWords = await File.ReadAllLinesAsync(fileName);
        await File.WriteAllLinesAsync(fileName, allWords.Select(w => w.ToLower()).Distinct().Order());
    }

    private static IWebDriver InstantiateDriver()
    {
        var chromeOptions = new ChromeOptions();

        // Starting the browser in headless mode should make everything faster.
        chromeOptions.AddArgument("headless");

        return new ChromeDriver("./chromedriver.exe", chromeOptions);
    }
}