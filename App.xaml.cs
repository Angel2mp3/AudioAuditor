using System.Windows;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.Initialize();
        }
    }
}
