namespace API.Features;

public class WordsRegister : IWordsRegister
{
    private readonly HashSet<string> _words =  new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
    public bool Contains(string word) => this._words.Contains(word);
    public void Register(string word) => this._words.Add(word);
}