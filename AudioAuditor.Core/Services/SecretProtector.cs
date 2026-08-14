using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AudioQualityChecker.Services;

/// <summary>
/// Encrypts and decrypts the credential blob at rest, using whatever the host OS provides.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Short name of the mechanism in use, for diagnostics.</summary>
    string Mechanism { get; }

    byte[] Protect(byte[] plaintext);

    /// <summary>Returns null when the blob cannot be decrypted (wrong user, key gone, corrupt).</summary>
    byte[]? Unprotect(byte[] ciphertext);
}

/// <summary>
/// Picks the protector for the running platform.
/// </summary>
/// <remarks>
/// Windows uses DPAPI over the whole file, which is byte-for-byte what the WPF build writes
/// (Services/ThemeManager.Persistence.cs:379) — the two builds must be able to read each
/// other's session.dat, so that format is fixed and cannot be "improved" here.
///
/// The other platforms have no DPAPI. They keep a random 32-byte master key in the OS
/// keychain and encrypt the file with AES-GCM under it: the keychain holds one small secret
/// instead of the whole blob, which is what its APIs are shaped for.
/// </remarks>
public static class SecretProtection
{
    private static ISecretProtector? _instance;

    public static ISecretProtector Current => _instance ??= Create();

    /// <summary>Overrides the protector. Test seam only.</summary>
    internal static void SetForTesting(ISecretProtector? protector) => _instance = protector;

    private static ISecretProtector Create()
    {
        // OperatingSystem.IsWindows rather than RuntimeInformation so the platform analyser
        // can see that the DPAPI calls below are guarded.
        if (OperatingSystem.IsWindows())
            return new DpapiProtector();

        if (OperatingSystem.IsMacOS())
            return new KeyedAesProtector(new MacKeychainKeyStore());

        return new KeyedAesProtector(new LibSecretKeyStore());
    }
}

/// <summary>Windows DPAPI, current-user scope. Same call the WPF build makes.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class DpapiProtector : ISecretProtector
{
    public string Mechanism => "DPAPI (CurrentUser)";

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

    public byte[]? Unprotect(byte[] ciphertext)
    {
        try { return ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser); }
        catch { return null; }
    }
}

/// <summary>Stores and retrieves the one master key the non-Windows protector needs.</summary>
internal interface IMasterKeyStore
{
    string Name { get; }

    /// <summary>Returns the stored key, or null when none has been stored yet.</summary>
    byte[]? Load();

    void Store(byte[] key);
}

/// <summary>
/// AES-GCM under a master key held by the OS keychain.
/// Layout: [1 byte version][12 byte nonce][16 byte tag][ciphertext].
/// </summary>
internal sealed class KeyedAesProtector : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int NonceLength = 12;   // AesGcm.NonceByteSizes.MaxSize
    private const int TagLength = 16;     // AesGcm.TagByteSizes.MaxSize
    private const int KeyLength = 32;

    private readonly IMasterKeyStore _keyStore;

    public KeyedAesProtector(IMasterKeyStore keyStore) => _keyStore = keyStore;

    public string Mechanism => $"AES-GCM ({_keyStore.Name})";

    public byte[] Protect(byte[] plaintext)
    {
        var key = _keyStore.Load();
        if (key is not { Length: KeyLength })
        {
            key = RandomNumberGenerator.GetBytes(KeyLength);
            _keyStore.Store(key);
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using (var aes = new AesGcm(key, TagLength))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[1 + NonceLength + TagLength + ciphertext.Length];
        output[0] = FormatVersion;
        nonce.CopyTo(output, 1);
        tag.CopyTo(output, 1 + NonceLength);
        ciphertext.CopyTo(output, 1 + NonceLength + TagLength);
        return output;
    }

    public byte[]? Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < 1 + NonceLength + TagLength) return null;
        if (ciphertext[0] != FormatVersion) return null;

        var key = _keyStore.Load();
        if (key is not { Length: KeyLength }) return null;

        var nonce = ciphertext.AsSpan(1, NonceLength);
        var tag = ciphertext.AsSpan(1 + NonceLength, TagLength);
        var body = ciphertext.AsSpan(1 + NonceLength + TagLength);
        var plaintext = new byte[body.Length];

        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, body, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // Wrong key or tampered file. Indistinguishable on purpose.
            return null;
        }
    }
}

/// <summary>
/// Linux Secret Service, via libsecret. Lands in GNOME Keyring / KWallet depending on the
/// desktop; both implement the same D-Bus interface behind this library.
/// </summary>
internal sealed class LibSecretKeyStore : IMasterKeyStore
{
    public string Name => "libsecret";

    // Attribute schema. Only the name matters for lookup; libsecret creates it on demand
    // when the flags say so, so no schema needs registering up front.
    private const string SchemaName = "com.angelsoftware.AudioAuditor";
    private const string AttributeKey = "purpose";
    private const string AttributeValue = "credential-store-master-key";
    private const string Label = "AudioAuditor credential store";

    public byte[]? Load()
    {
        try
        {
            var ptr = secret_password_lookup_sync(
                IntPtr.Zero, IntPtr.Zero, out var error,
                AttributeKey, AttributeValue, IntPtr.Zero);

            if (error != IntPtr.Zero || ptr == IntPtr.Zero) return null;

            try
            {
                var encoded = Marshal.PtrToStringUTF8(ptr);
                return string.IsNullOrEmpty(encoded) ? null : Convert.FromBase64String(encoded);
            }
            finally
            {
                secret_password_free(ptr);
            }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        catch (FormatException) { return null; }
    }

    public void Store(byte[] key)
    {
        try
        {
            secret_password_store_sync(
                IntPtr.Zero, SchemaName, Label, Convert.ToBase64String(key),
                IntPtr.Zero, out _,
                AttributeKey, AttributeValue, IntPtr.Zero);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("libsecret-1.so.0", CharSet = CharSet.Ansi)]
    private static extern IntPtr secret_password_lookup_sync(
        IntPtr schema, IntPtr cancellable, out IntPtr error,
        string attr1Name, string attr1Value, IntPtr terminator);

    [DllImport("libsecret-1.so.0", CharSet = CharSet.Ansi)]
    private static extern bool secret_password_store_sync(
        IntPtr schema, string collection, string label, string password,
        IntPtr cancellable, out IntPtr error,
        string attr1Name, string attr1Value, IntPtr terminator);

    [DllImport("libsecret-1.so.0")]
    private static extern void secret_password_free(IntPtr password);
}

/// <summary>macOS Keychain Services, generic password item.</summary>
internal sealed class MacKeychainKeyStore : IMasterKeyStore
{
    public string Name => "Keychain";

    private const string Service = "com.angelsoftware.AudioAuditor";
    private const string Account = "credential-store-master-key";

    private const int ErrSecSuccess = 0;
    private const int ErrSecDuplicateItem = -25299;

    public byte[]? Load()
    {
        try
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var account = Encoding.UTF8.GetBytes(Account);

            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero,
                (uint)service.Length, service,
                (uint)account.Length, account,
                out var length, out var data, IntPtr.Zero);

            if (status != ErrSecSuccess || data == IntPtr.Zero) return null;

            try
            {
                var key = new byte[length];
                Marshal.Copy(data, key, 0, (int)length);
                return key;
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
            }
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    public void Store(byte[] key)
    {
        try
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var account = Encoding.UTF8.GetBytes(Account);

            var status = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                (uint)service.Length, service,
                (uint)account.Length, account,
                (uint)key.Length, key, IntPtr.Zero);

            // An item already exists (a previous key we could not read, or a stale entry).
            // Replace it rather than leaving the new key unstored, which would make every
            // later Unprotect fail.
            if (status == ErrSecDuplicateItem)
            {
                var find = SecKeychainFindGenericPassword(
                    IntPtr.Zero,
                    (uint)service.Length, service,
                    (uint)account.Length, account,
                    out _, out var data, out var itemRef);

                if (find == ErrSecSuccess && itemRef != IntPtr.Zero)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, data);
                    SecKeychainItemModifyAttributesAndData(
                        itemRef, IntPtr.Zero, (uint)key.Length, key);
                }
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, IntPtr itemRef);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainFindGenericPassword")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
}
