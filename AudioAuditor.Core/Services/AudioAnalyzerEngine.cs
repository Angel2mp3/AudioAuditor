using AudioQualityChecker.Abstractions;
using AudioQualityChecker.Models;

namespace AudioQualityChecker.Services
{
    public sealed class AudioAnalyzerEngine
    {
        private readonly IAnalysisSettings _settings;

        public AudioAnalyzerEngine(IAnalysisSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public AudioFileInfo Analyze(string filePath, CancellationToken cancellationToken = default)
        {
            return AudioAnalyzer.AnalyzeFile(filePath, _settings, cancellationToken);
        }
    }
}
