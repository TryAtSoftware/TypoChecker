namespace Scraper;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;

public class WiktionaryScraper : IScraper
{
    public async Task ReadMainWords(IWebDriver driver, Stream resultStream, CancellationToken cancellationToken = default)
    {
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

            foreach (var word in allWords) await resultStream.WriteAsync(word.ToBytes(), cancellationToken);

            nextPageButton.Click();

            (containerElement, nextPageButton) = FindMainWordElements(driver);

            pageIndex++;
            stopwatch.Reset();
        }
    }

    public async Task ReadWordForms(IWebDriver driver, string word, Stream resultStream, CancellationToken cancellationToken = default)
    {
        var navigation = driver.Navigate();
        navigation.GoToUrl($"https://bg.wiktionary.org/wiki/{word}");

        var moreFormsAreShown = ShowMoreFormsIfPossible(driver);
        var wordForms = FindForms(driver);

        if (!moreFormsAreShown && wordForms.Length == 0)
        {
            navigation.GoToUrl($"https://bg.wiktionary.org/wiki/Шаблон:Словоформи/{word}");
            wordForms = FindForms(driver);
        }

        foreach (var wordForm in wordForms.Select(x => SanitizeWord(x.Text)).Where(x => !string.IsNullOrEmpty(x)))
            await resultStream.WriteAsync(wordForm.ToBytes(), cancellationToken);
    }

    private static (IWebElement Container, IWebElement? NextPageButton) FindMainWordElements(IWebDriver driver)
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

    private static bool ShowMoreFormsIfPossible(IWebDriver driver)
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

    private static IWebElement[] FindForms(IWebDriver driver) => driver.FindElements(By.CssSelector(".forms-table tbody > tr > td")).ToArray();
}