using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WinNotch.Music;

namespace WinNotch.Services;

/// <summary>
/// Stores media-provider secrets in the current user's Windows Credential
/// Manager vault, with an atomic current-user DPAPI file fallback when the
/// vault is unavailable. Provider tokens and passwords never enter settings.json.
/// </summary>
public sealed class WindowsCredentialStore : IMediaCredentialStore
{
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;
    private const int ErrorNoSuchLogonSession = 1312;
    private const int CryptProtectUiForbidden = 0x1;
    private const int MaximumSecretBytes = 2560;
    private readonly string _targetPrefix;
    private readonly string _fallbackPath;
    private readonly bool _useCredentialManager;
    private readonly object _fallbackGate = new();

    public WindowsCredentialStore(
        string targetPrefix = "WinNotch.MediaQueue",
        string? fallbackPath = null,
        bool useCredentialManager = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPrefix);
        _targetPrefix = targetPrefix.Trim();
        _useCredentialManager = useCredentialManager;
        _fallbackPath = Path.GetFullPath(fallbackPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinNotch",
            "media-credentials.bin"));
    }

    public string? Read(string key)
    {
        if (!_useCredentialManager) return ReadFallback(key);
        string target = GetTarget(key);
        if (!CredRead(target, GenericCredential, 0, out var pointer))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound || CanUseFallback(error))
                return ReadFallback(key);
            throw new Win32Exception(error, "Could not read the media credential securely.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return string.Empty;
            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string key, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        int byteCount = checked(secret.Length * sizeof(char));
        if (byteCount > MaximumSecretBytes)
            throw new ArgumentOutOfRangeException(nameof(secret), "Credential is too large for Windows Credential Manager.");

        if (!_useCredentialManager)
        {
            WriteFallback(key, secret);
            return;
        }

        IntPtr blob = secret.Length == 0
            ? IntPtr.Zero
            : Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = GetTarget(key),
                CredentialBlobSize = (uint)byteCount,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (CredWrite(ref credential, 0))
            {
                DeleteFallback(key);
                return;
            }
            int error = Marshal.GetLastWin32Error();
            if (!CanUseFallback(error))
                throw new Win32Exception(error, "Could not store the media credential securely.");
            WriteFallback(key, secret);
        }
        finally
        {
            if (blob != IntPtr.Zero)
                Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public void Delete(string key)
    {
        if (!_useCredentialManager)
        {
            DeleteFallback(key);
            return;
        }
        string target = GetTarget(key);
        if (CredDelete(target, GenericCredential, 0))
        {
            DeleteFallback(key);
            return;
        }
        int error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound && !CanUseFallback(error))
            throw new Win32Exception(error, "Could not delete the media credential securely.");
        DeleteFallback(key);
    }

    private string GetTarget(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return $"{_targetPrefix}.{key.Trim()}";
    }

    private static bool CanUseFallback(int error) =>
        error is ErrorAccessDenied or ErrorNotSupported or ErrorNoSuchLogonSession;

    private string? ReadFallback(string key)
    {
        lock (_fallbackGate)
            return ReadFallbackDictionary().GetValueOrDefault(GetTarget(key));
    }

    private void WriteFallback(string key, string secret)
    {
        lock (_fallbackGate)
        {
            var values = ReadFallbackDictionary();
            values[GetTarget(key)] = secret;
            SaveFallbackDictionary(values);
        }
    }

    private void DeleteFallback(string key)
    {
        lock (_fallbackGate)
        {
            var values = ReadFallbackDictionary();
            if (!values.Remove(GetTarget(key))) return;
            if (values.Count == 0)
            {
                File.Delete(_fallbackPath);
                return;
            }
            SaveFallbackDictionary(values);
        }
    }

    private Dictionary<string, string> ReadFallbackDictionary()
    {
        if (!File.Exists(_fallbackPath)) return new(StringComparer.Ordinal);
        try
        {
            byte[] protectedBytes = File.ReadAllBytes(_fallbackPath);
            byte[] jsonBytes = Unprotect(protectedBytes);
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(jsonBytes)
                       ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(jsonBytes);
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or Win32Exception)
        {
            throw new InvalidOperationException(
                "The encrypted media credential store could not be read.", exception);
        }
    }

    private void SaveFallbackDictionary(Dictionary<string, string> values)
    {
        string? directory = Path.GetDirectoryName(_fallbackPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The media credential path has no parent directory.");
        Directory.CreateDirectory(directory);
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(values);
        byte[] protectedBytes;
        try
        {
            protectedBytes = Protect(jsonBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(jsonBytes);
        }
        string temporaryPath = _fallbackPath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _fallbackPath, overwrite: true);
    }

    private static byte[] Protect(byte[] plaintext) => TransformData(plaintext, protect: true);
    private static byte[] Unprotect(byte[] ciphertext) => TransformData(ciphertext, protect: false);

    private static byte[] TransformData(byte[] input, bool protect)
    {
        if (input.Length == 0) return [];
        var inputBlob = new DataBlob();
        var outputBlob = new DataBlob();
        try
        {
            inputBlob.Size = input.Length;
            inputBlob.Data = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputBlob.Data, input.Length);
            bool success = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out outputBlob);
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    protect ? "Could not encrypt media credentials." : "Could not decrypt media credentials.");
            var result = new byte[outputBlob.Size];
            Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Size);
            return result;
        }
        finally
        {
            if (inputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[input.Length], 0, inputBlob.Data, input.Length);
                Marshal.FreeHGlobal(inputBlob.Data);
            }
            if (outputBlob.Data != IntPtr.Zero) LocalFree(outputBlob.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        int type,
        int flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
