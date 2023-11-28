namespace Core.Features;

public interface IWordsRegister
{
    bool Contains(string word);
    void Register(string word);
}