namespace API.Features
{
    public interface IWordsRegister
    {
        public bool Contains(string word);
        public Task InitializeAsync(CancellationToken cancellationToken);
    }
}
