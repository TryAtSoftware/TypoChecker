namespace Scraper;

using System;
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
        var chromeOptions = new ChromeOptions();

        // Starting the browser in headless mode should make everything faster.
        chromeOptions.AddArgument("headless");

        using var driver = new ChromeDriver("./chromedriver.exe", chromeOptions);

        // await ReadMainWordsAsync(driver);
        await FormatWordsList(MAIN_WORDS_LIST);
        
        // await ReadWordFormsAsync(driver);
    }

    private static async Task ReadMainWordsAsync(ChromeDriver driver)
    {
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

    private static async Task ReadWordFormsAsync(ChromeDriver driver)
    {
        // Ensure that we are always working with a brand new file.
        File.Delete(FORMS_WORDS_LIST);
        
        var navigation = driver.Navigate();
        var file = await File.ReadAllLinesAsync(MAIN_WORDS_LIST);
            
        for (var i = 0; i < file.Length; i++)
        {
            navigation.GoToUrl($"https://bg.wiktionary.org/wiki/{file[i]}");

            try
            {
                ShowMoreFormsIfPossible(driver);

                var wordsList = driver.FindElements(By.CssSelector("table.forms-table.plainlinks tbody > tr > td")).ToArray();
                var allWords = wordsList.Select(x => SanitizeWord(x.Text)).Where(x => !string.IsNullOrEmpty(x)).ToArray();

                await File.AppendAllLinesAsync(FORMS_WORDS_LIST, allWords);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unexpected exception occurred: {e.Message}");
            }
        }
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

    private static string SanitizeWord(string word)
        => word.Replace("·", string.Empty).Replace("—", string.Empty).Trim();

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
}