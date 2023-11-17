namespace Scraper;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Linq;

public static class Program
{
    private const string MAIN_WORDS_LIST = "main-words-list.txt";
    private const string FORMS_WORDS_LIST = "forms-words-list.txt";

    public static async Task Main()
    {
        await ReadMainWordsAsync();
        await FormatWordsList(MAIN_WORDS_LIST);

        await ReadWordFormsAsync(workers: 5);
        await FormatWordsList(FORMS_WORDS_LIST);
    }

    private static async Task ReadMainWordsAsync()
    {
        using var driver = InstantiateDriver();
        var navigation = driver.Navigate();
        navigation.GoToUrl("https://bg.wiktionary.org/w/index.php?title=Категория:Думи_в_българския_език");

        // Ensure that we are always working with a brand new file.
        File.Delete(MAIN_WORDS_LIST);

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

            await File.AppendAllLinesAsync(MAIN_WORDS_LIST, allWords);
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
        var wordForms = new ConcurrentBag<string>();

        try
        {
            var mainWords = await File.ReadAllLinesAsync(MAIN_WORDS_LIST);
            await Parallel.ForEachAsync(mainWords, new ParallelOptions { MaxDegreeOfParallelism = workers }, (word, _) => ScrapeWordFormsAsync(word, availableDrivers, wordForms));
            await File.WriteAllLinesAsync(FORMS_WORDS_LIST, wordForms);
        }
        finally
        {
            foreach (var driver in drivers) driver.Dispose();
        }
    }

    private static ValueTask ScrapeWordFormsAsync(string word, ConcurrentQueue<ChromeDriver> availableDrivers, ConcurrentBag<string> wordForms)
    {
        if (!availableDrivers.TryDequeue(out var driver)) return ValueTask.CompletedTask;

        var navigation = driver.Navigate();
        navigation.GoToUrl($"https://bg.wiktionary.org/wiki/{word}");

        ShowMoreFormsIfPossible(driver);

        var wordsList = driver.FindElements(By.CssSelector("table.forms-table.plainlinks tbody > tr > td")).ToArray();
        foreach (var wordForm in wordsList.Select(x => SanitizeWord(x.Text)).Where(x => !string.IsNullOrEmpty(x)))
            wordForms.Add(wordForm);

        availableDrivers.Enqueue(driver);
        return ValueTask.CompletedTask;
    }

    private static async Task FormatWordsList(string fileName)
    {
        var allWords = await File.ReadAllLinesAsync(fileName);
        await File.WriteAllLinesAsync(fileName, allWords.Select(w => w.ToLower().Split(' ', '-')).SelectMany(x => x).Distinct().Order());
    }

    private static void ShowMoreFormsIfPossible(ChromeDriver driver)
    {
        try
        {
            var showMoreLink = driver.FindElement(By.LinkText("Всички форми"));
            showMoreLink.Click();
        }
        catch (NotFoundException)
        {
            // There is not "Show more" link to be clicked.
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

    private static ChromeDriver InstantiateDriver()
    {
        var chromeOptions = new ChromeOptions();

        // Starting the browser in headless mode should make everything faster.
        chromeOptions.AddArgument("headless");

        return new ChromeDriver("./chromedriver.exe", chromeOptions);
    }
}