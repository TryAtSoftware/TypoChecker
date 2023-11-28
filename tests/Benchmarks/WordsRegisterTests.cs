namespace Benchmarks;

using BenchmarkDotNet.Attributes;
using Core.Features;

/*
| Method     | WordsList            | Mean    | Error    | StdDev   | Gen0       | Gen1       | Gen2      | Allocated |
|----------- |--------------------- |--------:|---------:|---------:|-----------:|-----------:|----------:|----------:|
| UseHashSet | Slovo(...)t.txt [29] | 1.183 s | 0.0318 s | 0.0908 s |  1000.0000 |  1000.0000 | 1000.0000 |  51.39 MB |
| UseTrie    | Slovo(...)t.txt [29] | 2.069 s | 0.0544 s | 0.1525 s | 70000.0000 | 50000.0000 | 3000.0000 | 536.06 MB |
*/
[MemoryDiagnoser]
public class WordsRegisterTests
{
    private string[] _words = Array.Empty<string>();

    [Params("Slovored/forms-words-list.txt")] public string WordsList { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        this._words = File.ReadAllLines(this.WordsList);
    }
    
    [Benchmark]
    public void UseHashSet() => this.Act(new SetWordsRegister());

    [Benchmark]
    public void UseTrie() => this.Act(new TrieWordsRegister());

    private void Act(IWordsRegister register)
    {
        foreach (var word in this._words) register.Register(word);
        foreach (var word in this._words) register.Contains(word);
    }
}