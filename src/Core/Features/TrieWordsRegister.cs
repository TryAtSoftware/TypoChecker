namespace Core.Features;

public class TrieWordsRegister : IWordsRegister
{
    private readonly TrieNode _rootNode = new TrieNode();

    public bool Contains(string word)
    {
        var node = this._rootNode;
        foreach (var letter in SanitizeWord(word))
        {
            if (!node.Children.TryGetValue(letter, out var next))
                return false;

            node = next;
        }

        return node.IsEnd;
    }

    public void Register(string word)
    {
        var node = this._rootNode;
        foreach (var letter in SanitizeWord(word))
        {
            if (!node.Children.TryGetValue(letter, out var next))
            {
                next = new TrieNode();
                node.Children[letter] = next;
            }

            node = next;
        }

        node.IsEnd = true;
    }

    private static IEnumerable<char> SanitizeWord(string word) => word.Select(char.ToLowerInvariant);
}

internal class TrieNode
{
    public IDictionary<char, TrieNode> Children { get; } = new Dictionary<char, TrieNode>();
    public bool IsEnd { get; set; }
}