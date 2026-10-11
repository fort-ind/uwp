using Windows.UI.Xaml;

namespace Fort.ind_UWP
{
    public sealed class SocialProfileFieldRow : BindableBase
    {
        private int _position;
        private bool _stacked;
        private string _error = "";

        public SocialProfileFieldRow(string name, string value)
        {
            Name = name ?? "";
            Value = value ?? "";
        }

        public string Name { get; set; }

        public string Value { get; set; }

        public int Position
        {
            get { return _position; }
            set
            {
                if (!SetProperty(ref _position, value)) return;
                OnPropertyChanged(nameof(LabelBoxName));
                OnPropertyChanged(nameof(ValueBoxName));
                OnPropertyChanged(nameof(RemoveButtonName));
            }
        }

        public bool Stacked
        {
            get { return _stacked; }
            set
            {
                if (!SetProperty(ref _stacked, value)) return;
                OnPropertyChanged(nameof(LabelColumnSpan));
                OnPropertyChanged(nameof(ValueRow));
                OnPropertyChanged(nameof(ValueColumn));
                OnPropertyChanged(nameof(ValueColumnSpan));
                OnPropertyChanged(nameof(ValueMargin));
            }
        }

        public string Error
        {
            get { return _error; }
            set
            {
                if (SetProperty(ref _error, value ?? "")) OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError
        {
            get { return _error.Length > 0; }
        }

        public int LabelColumnSpan
        {
            get { return _stacked ? 2 : 1; }
        }

        public int ValueRow
        {
            get { return _stacked ? 1 : 0; }
        }

        public int ValueColumn
        {
            get { return _stacked ? 0 : 1; }
        }

        public int ValueColumnSpan
        {
            get { return _stacked ? 2 : 1; }
        }

        public Thickness ValueMargin
        {
            get { return _stacked ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0); }
        }

        public string LabelBoxName
        {
            get { return LocalizedStrings.Format("SocialProfileFieldLabelNameFormat", _position); }
        }

        public string ValueBoxName
        {
            get { return LocalizedStrings.Format("SocialProfileFieldValueNameFormat", _position); }
        }

        public string RemoveButtonName
        {
            get { return LocalizedStrings.Format("SocialProfileFieldRemoveNameFormat", _position); }
        }
    }
}
