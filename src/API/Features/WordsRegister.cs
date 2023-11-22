
namespace API.Features
{
    public class WordsRegister : IWordsRegister
    {
        private readonly HashSet<string> _words =  new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        public bool Contains(string word) => this._words.Contains(word);

        public async Task InitializeAsync(CancellationToken cancellationToken)
        { 
            //TODO: fix multithreading issues
            await LoadWordsAsync("forms-words-list.txt", cancellationToken);
            await LoadWordsAsync("main-words-list.txt", cancellationToken);
        }

        private async Task LoadWordsAsync(string fileName, CancellationToken cancellationToken)
        {
            var pathToFile = Path.Combine(AppContext.BaseDirectory, fileName);
            var words = await File.ReadAllLinesAsync(pathToFile);
            foreach (var word in words) this._words.Add(word);
        }
    }
}
