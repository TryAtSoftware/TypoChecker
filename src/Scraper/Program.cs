namespace Scraper
{
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

        public static async Task Main()
        {
            var chromeOptions = new ChromeOptions();

            // Starting the browser in headless mode should make everything faster.
            chromeOptions.AddArgument("headless");

            using var driver = new ChromeDriver("./chromedriver.exe", chromeOptions);

            await ReadMainWordsAsync(driver);
            await ReadWordFormsAsync(driver);
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
            // TODO: Make Async
            INavigation navigation = driver.Navigate();
            string[] file = File.ReadAllLines(MAIN_WORDS_LIST);
            for (int i = 0; i < file.Length; i++)
            {
                navigation.GoToUrl($"https://bg.wiktionary.org/wiki/{file[i]}");

                try
                {
                    try
                    {
                        IWebElement showMoreLink = driver.FindElement(By.LinkText("Всички форми"));
                        showMoreLink.Click();
                    }
                    catch (NotFoundException)
                    {
                        //
                    }

                    IWebElement containerElement = driver.FindElement(By.CssSelector("table.forms-table.plainlinks"));

                    IWebElement[] wordsList = containerElement.FindElements(By.CssSelector("tbody > tr > td")).ToArray();
                    string[] allWords = wordsList.Select(x => SanitizeWord(x.Text)).Where(x => !string.IsNullOrEmpty(x) && x != "—").ToArray();

                    Console.WriteLine(string.Join(Environment.NewLine, allWords));
                }
                catch (NotFoundException)
                {
                    // 
                }
            }
        }

        private static string SanitizeWord(string word)
        {
            return word.Replace("·", string.Empty).Trim();
        }

        private static (IWebElement Container, IWebElement? NextPageButton) FindMainWordElements(ChromeDriver driver)
        {
            var containerElement = driver.FindElement(By.CssSelector("div#mw-pages"));
            IWebElement? nextPageButton;

            try
            {
                nextPageButton = containerElement.FindElement(By.LinkText("следваща страница"));
            }
            catch
            {
                nextPageButton = null;
            }

            return (containerElement, nextPageButton);
        }
    }
}