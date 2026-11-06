using System.ComponentModel;
using LegacyShelf.Models;

namespace LegacyShelf.Controls
{
    public sealed class TransportGameItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _sizeText;

        public Game Game { get; private set; }

        public TransportGameItem(Game game, bool selected)
        {
            Game = game;
            _isSelected = selected;
            _sizeText = "calculating…";
        }

        public string Name
        {
            get { return Game.Name; }
        }

        public string FolderName
        {
            get { return Game.FolderName; }
        }

        public string ExeName
        {
            get { return Game.ExeName; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                Notify("IsSelected");
            }
        }

        public string SizeText
        {
            get { return _sizeText; }
            set
            {
                if (_sizeText == value) return;
                _sizeText = value;
                Notify("SizeText");
            }
        }

        public long SizeBytes { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
