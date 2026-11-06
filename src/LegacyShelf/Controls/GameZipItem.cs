using System.ComponentModel;
using LegacyShelf.Models;

namespace LegacyShelf.Controls
{
    public sealed class GameZipItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public GameZipCandidate Candidate { get; private set; }

        public GameZipItem(GameZipCandidate candidate, bool selected)
        {
            Candidate = candidate;
            _isSelected = selected;
        }

        public string Name
        {
            get { return Candidate.DisplayName; }
        }

        public string FolderName
        {
            get { return Candidate.FolderName; }
        }

        public string SizeText
        {
            get { return FormatBytes(Candidate.ZipBytes); }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                var handler = PropertyChanged;
                if (handler != null) handler(this, new PropertyChangedEventArgs("IsSelected"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var value = (double)bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit >= 3 ? "0.0" : "0") + " " + units[unit];
        }
    }
}
