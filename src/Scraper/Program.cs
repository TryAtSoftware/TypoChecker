namespace Scraper;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Linq;
using System.Text;

public static class Program
{
    private const int BUFFER_SIZE = 1024;
    
    private const string MAIN_WORDS_LIST = "main-words-list.txt";
    private const string FORMS_WORDS_LIST = "forms-words-list.txt";

    public static async Task Main()
    {
        await ReadMainWordsAsync();
        await FormatWordsList(MAIN_WORDS_LIST);

        await ReadWordFormsAsync(workers: Environment.ProcessorCount);
        await FormatWordsList(FORMS_WORDS_LIST);
    }

    private static async Task ReadMainWordsAsync()
    {
        await using var fileStream = new FileStream(MAIN_WORDS_LIST, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: BUFFER_SIZE, useAsync: true);

        using var driver = InstantiateDriver();
        var navigation = driver.Navigate();
        navigation.GoToUrl("https://bg.wiktionary.org/w/index.php?title=Категория:Думи_в_българския_език");

        var pageIndex = 1;
        var stopwatch = new Stopwatch();

        var (containerElement, nextPageButton) = FindMainWordElements(driver);
        while (nextPageButton is not null)
        {
            Console.WriteLine($"Started scraping page #{pageIndex}");
            stopwatch.Start();

            var wordsList = containerElement.FindElements(By.CssSelector("div.mw-category-group ul > li")).ToArray();
            var allWords = wordsList.Select(x => x.Text).ToArray();

            stopwatch.Stop();
            Console.WriteLine($"Finished scraping page #{pageIndex} in {stopwatch.ElapsedMilliseconds} ms. Found {allWords.Length} new words");

            foreach (var word in allWords) await fileStream.WriteAsync(Encoding.UTF8.GetBytes(word + Environment.NewLine));

            nextPageButton.Click();

            (containerElement, nextPageButton) = FindMainWordElements(driver);

            pageIndex++;
            stopwatch.Reset();
        }
    }

    private static async Task ReadWordFormsAsync(int workers)
    {
        var drivers = new ChromeDriver[workers];
        for (var i = 0; i < workers; i++) drivers[i] = InstantiateDriver();
        
        var availableDrivers = new ConcurrentQueue<ChromeDriver>(drivers);

        try
        {
            await using var fileStream = new FileStream(FORMS_WORDS_LIST, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: BUFFER_SIZE, useAsync: true);
            
            var mainWords = await File.ReadAllLinesAsync(MAIN_WORDS_LIST);
            await Parallel.ForEachAsync(mainWords, new ParallelOptions { MaxDegreeOfParallelism = workers }, (word, _) => ScrapeWordFormsAsync(word, availableDrivers, fileStream));
        }
        finally
        {
            foreach (var driver in drivers) driver.Dispose();
        }
    }

    private static async ValueTask ScrapeWordFormsAsync(string word, ConcurrentQueue<ChromeDriver> availableDrivers, Stream stream)
    {
        if (!availableDrivers.TryDequeue(out var driver)) return;

        var navigation = driver.Navigate();
        navigation.GoToUrl($"https://bg.wiktionary.org/wiki/{word}");

        var moreFormsAreShown = ShowMoreFormsIfPossible(driver);
        var wordForms = FindForms(driver);

        if (wordForms.Length == 0)
        {
            if (!moreFormsAreShown && wordForms.Length == 0)
            {
                navigation.GoToUrl($"https://bg.wiktionary.org/wiki/Шаблон:Словоформи/{word}");
                wordForms = FindForms(driver);
            }

            foreach (var wordForm in wordForms.Select(x => SanitizeWord(x.Text)).Where(x => !string.IsNullOrEmpty(x)))
                await stream.WriteAsync(Encoding.UTF8.GetBytes(wordForm + Environment.NewLine));
        }

        availableDrivers.Enqueue(driver);
    }

    private static async Task FormatWordsList(string fileName)
    {
        var allWords = await File.ReadAllLinesAsync(fileName);
        await File.WriteAllLinesAsync(fileName, allWords.Select(w => w.ToLower().Split(' ', '-')).SelectMany(x => x).Distinct().Order());
    }

    private static bool ShowMoreFormsIfPossible(ChromeDriver driver)
    {
        try
        {
            var showMoreLink = driver.FindElement(By.LinkText("Всички форми"));
            showMoreLink.Click();
            return true;
        }
        catch (NotFoundException)
        {
            // There is not "Show more" link to be clicked.
            return false;
        }
    }

    private static string SanitizeWord(string word) => word.Replace("·", string.Empty).Replace("—", string.Empty).Trim();

    private static (IWebElement Container, IWebElement? NextPageButton) FindMainWordElements(ChromeDriver driver)
    {
        var containerElement = driver.FindElement(By.CssSelector("div#mw-pages"));
        IWebElement? nextPageButton;

        try
        {
            nextPageButton = containerElement.FindElement(By.LinkText("следваща страница"));
        }
        catch (NotFoundException)
        {
            nextPageButton = null;
        }

        return (containerElement, nextPageButton);
    }

    private static IWebElement[] FindForms(ChromeDriver driver) => driver.FindElements(By.CssSelector(".forms-table tbody > tr > td")).ToArray();

    private static ChromeDriver InstantiateDriver()
    {
        var chromeOptions = new ChromeOptions();

        // Starting the browser in headless mode should make everything faster.
        chromeOptions.AddArgument("headless");

        return new ChromeDriver("./chromedriver.exe", chromeOptions);
    }
}