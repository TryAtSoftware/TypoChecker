namespace API.Models;

public class FileStatistics
{
    public required string FileName { get; init; }
    public required int Pages { get; init; }
    public required ulong TotalWords { get; init; }
    public required ulong IncorrectWords { get; init; }
    public required decimal IncorrectWordsPercentage { get; init; }
    public required ulong UnreadableWords { get; init; }
    public required decimal UnreadableWordsPercentage { get; init; }
}