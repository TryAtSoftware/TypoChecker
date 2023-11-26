namespace Scraper;

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;

public class SlovoredScraper : IScraper
{
    private static readonly Regex _wordFormsRegex = new (@"(?<=^\s{3})\w+", RegexOptions.Multiline);
    private static readonly string _categoriesSeparator = $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}";
    
    public async Task ReadMainWords(IWebDriver driver, Stream resultStream, CancellationToken cancellationToken = default)
    {
        var navigation = driver.Navigate();
        navigation.GoToUrl("https://slovored.com/search/pravopisen-rechnik/а");

        IWebElement? nextPageButton = null;

        do
        {
            if (nextPageButton is not null)
            {
                var action =  new Actions(driver);
                action.MoveToElement(nextPageButton, 0, 5).Click();
                action.Build().Perform();
                
                // Some custom JS code will be executed, so we need to wait here.
                await Task.Delay(1000, cancellationToken);
            }
            
            var words = driver.FindElements(By.CssSelector("div#wordsList > a"));
            foreach (var word in words) await resultStream.WriteAsync(word.Text.ToBytes(), cancellationToken);
            
            nextPageButton = driver.FindElement(By.CssSelector("button#nextWords"));
        } while (nextPageButton.Enabled);
    }

    public async Task ReadWordForms(IWebDriver driver, string word, Stream resultStream, CancellationToken cancellationToken = default)
    {
        var navigation = driver.Navigate();
        navigation.GoToUrl($"https://slovored.com/search/pravopisen-rechnik/{word}");

        var informationElement = driver.FindElement(By.TagName("pre"));
        var informationContents = informationElement.Text.Split(_categoriesSeparator);

        foreach (var informationContent in informationContents)
        {
            // Some words are both adjectives and verbs (e.g. "сбит") - in this case we need to use only the adjective forms!
            var hasSuperlatives = informationContent.Contains("прилагателно име") || informationContent.Contains("наречие");
            if (!hasSuperlatives) return;
            
            foreach (var wordForm in _wordFormsRegex.Matches(informationContent).Select(x => x.Value))
            {
                await resultStream.WriteAsync(wordForm.ToBytes(), cancellationToken);
                
                if (hasSuperlatives)
                {
                    await resultStream.WriteAsync($"по-{wordForm}".ToBytes(), cancellationToken);
                    await resultStream.WriteAsync($"най-{wordForm}".ToBytes(), cancellationToken);
                }
            }
        }
    }
}