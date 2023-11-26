namespace API.Settings
{
    public class DocumentIntelligenceSettings
    {
        public const string Section = "DocumentIntelligence";
        public required string APIKey { get; set; }
        public required string Endpoint { get; set; }
    }
}
