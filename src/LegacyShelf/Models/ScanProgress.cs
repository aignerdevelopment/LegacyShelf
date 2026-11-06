namespace LegacyShelf.Models
{
    public sealed class ScanProgress
    {
        public int Percent { get; set; }
        public string Message { get; set; }

        public ScanProgress()
        {
            Message = string.Empty;
        }

        public ScanProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }
    }
}
