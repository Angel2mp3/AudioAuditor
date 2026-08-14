using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioQualityChecker.Models;

namespace AudioQualityChecker
{
    /// <summary>
    /// One proposed rename shown in the Batch Editor's Rename grid: the file, its current name, the
    /// name it would get, and why. <see cref="NewName"/> is editable in the grid, so the applied
    /// rename comes from <see cref="TargetPath"/> after the edit is committed.
    ///
    /// Editing a row rewrites <see cref="Confidence"/> and <see cref="Reason"/> in code, and those
    /// columns are read-only bindings — without change notification the grid would keep showing the
    /// pre-edit "Skip" while the rename actually went ahead.
    /// </summary>
    public class RenamePreviewItem : INotifyPropertyChanged
    {
        private string _newName = "";
        private string _targetPath = "";
        private string _confidence = "";
        private string _reason = "";

        public AudioFileInfo? File { get; set; }
        public string CurrentName { get; set; } = "";
        public string Arrow { get; set; } = "→";

        public string NewName
        {
            get => _newName;
            set => Set(ref _newName, value);
        }

        public string TargetPath
        {
            get => _targetPath;
            set => Set(ref _targetPath, value);
        }

        public string Confidence
        {
            get => _confidence;
            set => Set(ref _confidence, value);
        }

        public string Reason
        {
            get => _reason;
            set => Set(ref _reason, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set(ref string field, string value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
