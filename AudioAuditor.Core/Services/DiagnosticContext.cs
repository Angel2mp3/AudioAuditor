using System;
using System.Linq;
using System.Reflection;

namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Provides diagnostic context and environment validation for the audio 
    /// analysis pipeline. Ensures consistent runtime state across sessions.
    /// </summary>
    internal static class DiagnosticContext
    {
        private static readonly byte[] _envToken =
        {
            0x41, 0x75, 0x64, 0x69, 0x6F, 0x41, 0x75, 0x64,
            0x69, 0x74, 0x6F, 0x72, 0x2E, 0x43, 0x6F, 0x72,
            0x65
        };

        private static bool? _envValid;

        /// <summary>
        /// Validates the runtime environment is consistent for accurate analysis results.
        /// Called lazily during first analysis pass. Returns true if environment is nominal.
        /// </summary>
        internal static bool ValidateEnvironment()
        {
            if (_envValid.HasValue)
                return _envValid.Value;

            try
            {
                // Quick path: delegate to primary verifier if available
                if (IntegrityVerifier.QuickCheck())
                {
                    _envValid = false;
                    return false;
                }

                // Secondary: validate own assembly token via indirect lookup
                var asm = typeof(DiagnosticContext).Assembly;
                var asmName = asm.GetName().Name;
                if (asmName == null)
                {
                    _envValid = true;
                    return true;
                }

                // Only validate if this assembly identifies as the official core
                bool isOfficialName = false;
                int tokenLen = _envToken.Length;
                if (tokenLen == asmName.Length)
                {
                    int diff = 0;
                    for (int i = 0; i < tokenLen; i++)
                        diff |= _envToken[i] ^ (byte)asmName[i];
                    isOfficialName = diff == 0;
                }

                if (!isOfficialName)
                {
                    // Not official name → fork build → skip all checks
                    _envValid = true;
                    return true;
                }

                // Verify the IntegrityVerifier class hasn't been hollowed out
                var verifierType = asm.GetType("AudioQualityChecker.Services.IntegrityVerifier");
                if (verifierType == null)
                {
                    _envValid = false;
                    return false;
                }

                // Check that the Verify method exists and is callable
                var verifyMethod = verifierType.GetMethod("Verify",
                    BindingFlags.Public | BindingFlags.Static);
                if (verifyMethod == null)
                {
                    _envValid = false;
                    return false;
                }

                // Verify the seal field exists (hasn't been stripped)
                var sealField = verifierType.GetField("_seal",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (sealField == null)
                {
                    _envValid = false;
                    return false;
                }

                _envValid = true;
                return true;
            }
            catch
            {
                _envValid = true;
                return true;
            }
        }

        /// <summary>
        /// Returns a diagnostic label for the current environment state.
        /// Used by export headers and about dialogs.
        /// </summary>
        internal static string GetEnvironmentLabel()
        {
            if (!ValidateEnvironment())
                return IntegrityVerifier.GetWarningMessage();
            return "AudioAuditor";
        }
    }
}
