namespace AudioQualityChecker.Services
{
    /// <summary>
    /// Build-time stand-in for the gitignored <c>Services/SHLabsSecrets.cs</c>, which holds the
    /// HMAC key that signs requests to the AudioAuditor SH Labs proxy. That key is a shared secret
    /// between the official builds and the proxy, so it is not part of the public distribution.
    ///
    /// The csproj compiles this file only when the real one is absent, so a fresh clone builds.
    /// Proxy requests from such a build fail signature verification, which is the intended
    /// behaviour — point <c>SHLabsDetectionService.CustomApiKey</c> at your own SH Labs key to
    /// bypass the proxy entirely (see the Settings window's SH Labs section).
    /// </summary>
    internal static class SHLabsSecrets
    {
        internal const string HmacKeyBase64 = "";
    }
}
