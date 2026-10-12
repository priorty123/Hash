namespace HashDash.Models
{
    public class AutomationConfig
    {
        public bool IsEnabled { get; set; }
        public string WatchFolder { get; set; } = "";
        public string TriggerFilePattern { get; set; } = "report.xml";
        public bool IncludeSubdirectories { get; set; } = true;
        public int  DelayMinutes { get; set; } = 2;
        public bool DelayEnabled { get; set; } = true;
    }
}