using Newtonsoft.Json;

namespace API
{
    public class FileStats
    {
        public required string FileName { get; set; }
        public required int TotalWords { get; set; }
        public required int IncorrectWords { get; set; }
        public required double PercentIncorrectWords { get; set; }
        public required int UnreadableWords { get; set; }
        public required double PercentUnreadableWords { get; set; }
    }
}
