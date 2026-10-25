using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Security;

internal enum CookieVaultError
{
    None,
    KeyMissing,
    KeyUnreadable,
    KeyInvalid,
    KeyDecryptFailed,
    KeyMismatch,
    FileFormat,
    FileDecryptFailed,
    HwidUnavailable
}

internal static class CookieVault
{
    internal const string FormatName = "FufuLauncher.CookieVault";
    private const int FormatVersion = 1;
    private const string AlgorithmName = "AES-256-GCM";
    private const string KeyProtectionName = "DPAPI";
    private const string IntermediateKeyProtectionName = "DPAPI+HWID";
    private const string LegacyKeyProtectionName = "DPAPI-CurrentUser";
    private const int KeySize = 32;
    private const int KeyIdSize = 8;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int LengthSize = 4;
    private const byte KeyFileVersion = 2;
    private const byte LegacyKeyFileVersion = 1;
    private const byte KeyFlagDpapi = 1;
    private const byte KeyFlagHwid = 2;
    private const int KeyHeaderSize = 4 + 1 + KeyIdSize;
    private const string BinaryFormatName = "FufuLauncher.CookieVault.Binary";
    private const int BinaryFormatVersion = 1;
    private const int MachineMagicSize = 8;
    private const int LegacyBinaryMagicSize = 4;
    private const int BinaryHeaderSize = MachineMagicSize + 1 + KeyIdSize + NonceSize + TagSize;
    private const int LegacyBinaryHeaderSize = LegacyBinaryMagicSize + 1 + KeyIdSize + NonceSize + TagSize;
    private const int HwidIterations = 100_000;
    private static readonly byte[] KeyMagic = Encoding.UTF8.GetBytes("FLVK");
    private static readonly byte[] LegacyBinaryMagic = Encoding.UTF8.GetBytes("FLCK");
    private static readonly Lazy<byte[]> MachineMagic = new(CreateMachineMagic);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FufuLauncher.CookieVault.DPAPI.v1");
    private static readonly byte[] HwidSalt = Encoding.UTF8.GetBytes("FufuLauncher.CookieVault.HWID.v1");
    private static readonly object KeySync = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        IntPtr lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        IntPtr lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    public static bool IsEncryptedFile(byte[] data) =>
        IsBinaryEnvelope(data) || IsLegacyJsonEnvelope(data);

    internal static bool IsLegacyJsonEnvelope(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("format", out var format)
                   && format.ValueKind == JsonValueKind.String
                   && string.Equals(format.GetString(), FormatName, StringComparison.Ordinal)
                   && root.TryGetProperty("nonce", out var nonce)
                   && nonce.ValueKind == JsonValueKind.String
                   && root.TryGetProperty("tag", out var tag)
                   && tag.ValueKind == JsonValueKind.String
                   && root.TryGetProperty("ciphertext", out var ciphertext)
                   && ciphertext.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsBinaryEnvelope(byte[] data) =>
        IsMachineBinaryEnvelope(data) || IsLegacyBinaryEnvelope(data);

    private static bool IsMachineBinaryEnvelope(byte[] data) =>
        data.Length > BinaryHeaderSize
        && data.AsSpan(0, MachineMagicSize).SequenceEqual(MachineMagic.Value);

    internal static bool IsLegacyBinaryEnvelope(byte[] data) =>
        !IsMachineBinaryEnvelope(data)
        && data.Length > LegacyBinaryHeaderSize
        && data.AsSpan(0, LegacyBinaryMagicSize).SequenceEqual(LegacyBinaryMagic);

    public static byte[] Encrypt(string dataDir, string fileName, byte[] plaintext)
    {
        var key = GetOrCreateKey(dataDir);
        byte[]? associatedData = null;
        var ciphertext = new byte[plaintext.Length];
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            associatedData = BuildBinaryAssociatedData(fileName, key.KeyId);

            using (var aes = new AesGcm(key.KeyBytes, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            var magic = MachineMagic.Value;
            var payload = new byte[BinaryHeaderSize + ciphertext.Length];
            magic.CopyTo(payload, 0);
            payload[MachineMagicSize] = BinaryFormatVersion;
            key.KeyId.CopyTo(payload, MachineMagicSize + 1);

            int offset = MachineMagicSize + 1 + KeyIdSize;
            nonce.CopyTo(payload, offset);
            offset += NonceSize;
            tag.CopyTo(payload, offset);
            offset += TagSize;
            ciphertext.CopyTo(payload, offset);

            return payload;
        }
        finally
        {
            Zero(ciphertext);
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    public static byte[] Decrypt(string dataDir, string fileName, byte[] fileBytes)
    {
        if (IsBinaryEnvelope(fileBytes))
            return DecryptBinary(dataDir, fileName, fileBytes);

        CookieFileEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CookieFileEnvelope>(fileBytes)
                       ?? throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密格式无效");
        }
        catch (JsonException ex)
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密格式无效", ex);
        }

        if (!string.Equals(envelope.Format, FormatName, StringComparison.Ordinal)
            || envelope.Version != FormatVersion
            || !string.Equals(envelope.Algorithm, AlgorithmName, StringComparison.Ordinal)
            || (!string.Equals(envelope.KeyProtection, KeyProtectionName, StringComparison.Ordinal)
                && !string.Equals(envelope.KeyProtection, IntermediateKeyProtectionName, StringComparison.Ordinal)
                && !string.Equals(envelope.KeyProtection, LegacyKeyProtectionName, StringComparison.Ordinal)))
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密格式不受支持");

        if (string.IsNullOrEmpty(envelope.KeyId)
            || string.IsNullOrEmpty(envelope.Nonce)
            || string.IsNullOrEmpty(envelope.Tag)
            || string.IsNullOrEmpty(envelope.Ciphertext))
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密参数缺失");

        byte[] keyId;
        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            keyId = Convert.FromBase64String(envelope.KeyId);
            nonce = Convert.FromBase64String(envelope.Nonce);
            tag = Convert.FromBase64String(envelope.Tag);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        }
        catch (FormatException ex)
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密参数编码无效", ex);
        }

        if (keyId.Length != KeyIdSize || nonce.Length != NonceSize || tag.Length != TagSize)
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 文件加密参数长度无效");

        var key = LoadKey(dataDir);
        byte[]? associatedData = null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(key.KeyId, keyId))
                throw new CookieVaultException(CookieVaultError.KeyMismatch, "Cookie 文件加密密钥不匹配");

            associatedData = BuildAssociatedData(fileName, keyId, envelope.Version, envelope.KeyProtection!);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key.KeyBytes, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                return plaintext;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "Cookie 文件完整性校验失败", ex);
            }
        }
        finally
        {
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    private static byte[] DecryptBinary(string dataDir, string fileName, byte[] fileBytes)
    {
        int magicSize;
        int headerSize;
        if (IsMachineBinaryEnvelope(fileBytes))
        {
            magicSize = MachineMagicSize;
            headerSize = BinaryHeaderSize;
        }
        else if (IsLegacyBinaryEnvelope(fileBytes))
        {
            magicSize = LegacyBinaryMagicSize;
            headerSize = LegacyBinaryHeaderSize;
        }
        else
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 二进制文件格式无效");
        }

        if (fileBytes.Length <= headerSize
            || fileBytes[magicSize] != BinaryFormatVersion)
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 二进制文件格式无效");

        int keyIdOffset = magicSize + 1;
        var keyId = fileBytes.AsSpan(keyIdOffset, KeyIdSize).ToArray();
        int nonceOffset = keyIdOffset + KeyIdSize;
        var nonce = fileBytes.AsSpan(nonceOffset, NonceSize).ToArray();
        int tagOffset = nonceOffset + NonceSize;
        var tag = fileBytes.AsSpan(tagOffset, TagSize).ToArray();
        int cipherOffset = tagOffset + TagSize;
        var ciphertext = fileBytes.AsSpan(cipherOffset).ToArray();

        if (ciphertext.Length == 0)
            throw new CookieVaultException(CookieVaultError.FileFormat, "Cookie 二进制文件格式无效");

        var key = LoadKey(dataDir);
        byte[]? associatedData = null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(key.KeyId, keyId))
                throw new CookieVaultException(CookieVaultError.KeyMismatch, "Cookie 文件加密密钥不匹配");

            associatedData = BuildBinaryAssociatedData(fileName, keyId);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key.KeyBytes, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                return plaintext;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "Cookie 文件完整性校验失败", ex);
            }
        }
        finally
        {
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    public static void DeleteKey(string dataDir)
    {
        string keyPath = GetKeyPath(dataDir);
        lock (KeySync)
        {
            try
            {
                if (!File.Exists(keyPath))
                    return;

                try
                {
                    long length = new FileInfo(keyPath).Length;
                    if (length > 0 && length <= int.MaxValue)
                    {
                        var scrub = RandomNumberGenerator.GetBytes((int)length);
                        try
                        {
                            using var stream = new FileStream(keyPath, FileMode.Open, FileAccess.Write, FileShare.None);
                            stream.Write(scrub, 0, scrub.Length);
                            stream.Flush(true);
                        }
                        finally
                        {
                            Zero(scrub);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("CookieVault",
                        $"[CookieVault] Failed to overwrite Cookie encryption key: {ex.Message}");
                }

                File.Delete(keyPath);

                string? directory = Path.GetDirectoryName(keyPath);
                if (!string.IsNullOrEmpty(directory)
                    && Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] Failed to delete Cookie encryption key: {ex.Message}");
            }
        }
    }

    public static void EnsureKeyProtectionUpgrade(string dataDir)
    {
        lock (KeySync)
        {
            try
            {
                string keyPath = GetKeyPath(dataDir);
                if (!File.Exists(keyPath))
                    return;

                var fileBytes = File.ReadAllBytes(keyPath);
                try
                {
                    var parsed = ParseKeyFile(fileBytes);
                    try
                    {
                        if (parsed.Version == KeyFileVersion
                            && parsed.HwidCipher != null
                            && parsed.DpapiBlob == null)
                        {
                            Zero(UnwrapKeyData(parsed));
                            return;
                        }

                        if (parsed.Version == KeyFileVersion
                            && parsed.DpapiBlob != null
                            && TryUnprotectDpapi(parsed.DpapiBlob, out var existingKey))
                        {
                            Zero(existingKey);
                            return;
                        }

                        var keyBytes = UnwrapKeyData(parsed);
                        if (keyBytes == null)
                            return;

                        try
                        {
                            var payload = BuildKeyFileV2(parsed.KeyId, keyBytes,
                                parsed.Version == LegacyKeyFileVersion);
                            try
                            {
                                WriteKeyFile(keyPath, payload, overwrite: true);
                                DebugLog.Info("CookieVault", "[CookieVault] Cookie encryption key protection upgraded");
                            }
                            finally
                            {
                                Zero(payload);
                            }
                        }
                        finally
                        {
                            Zero(keyBytes);
                        }
                    }
                    finally
                    {
                        parsed.Clear();
                    }
                }
                finally
                {
                    Zero(fileBytes);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("CookieVault",
                    $"[CookieVault] Failed to upgrade Cookie encryption key protection: {ex.Message}");
            }
        }
    }

    private static CookieKey GetOrCreateKey(string dataDir)
    {
        string keyPath = GetKeyPath(dataDir);
        lock (KeySync)
        {
            if (File.Exists(keyPath))
            {
                try
                {
                    return LoadKey(dataDir);
                }
                catch (CookieVaultException ex) when (ex.Error is CookieVaultError.KeyMissing
                                                          or CookieVaultError.KeyInvalid
                                                          or CookieVaultError.KeyDecryptFailed)
                {
                    QuarantineKeyFile(keyPath);
                }
            }

            return CreateKey(keyPath);
        }
    }

    private static CookieKey CreateKey(string keyPath)
    {
        string? directory = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrEmpty(directory))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥路径无效");

        byte[]? keyBytes = null;
        byte[]? payload = null;
        try
        {
            Directory.CreateDirectory(directory);
            keyBytes = RandomNumberGenerator.GetBytes(KeySize);
            var keyId = RandomNumberGenerator.GetBytes(KeyIdSize);
            payload = BuildKeyFileV2(keyId, keyBytes, allowDpapi: true);
            WriteKeyFile(keyPath, payload, overwrite: false);

            var key = new CookieKey(keyBytes, keyId);
            keyBytes = null;
            return key;
        }
        catch (IOException) when (File.Exists(keyPath))
        {
            return LoadKeyFile(keyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥无法创建", ex);
        }
        finally
        {
            Zero(keyBytes);
            Zero(payload);
        }
    }

    private static CookieKey LoadKey(string dataDir) => LoadKeyFile(GetKeyPath(dataDir));

    private static CookieKey LoadKeyFile(string keyPath)
    {
        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(keyPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CookieVaultException(CookieVaultError.KeyMissing, "Cookie 加密密钥不存在", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CookieVaultException(CookieVaultError.KeyUnreadable, "Cookie 加密密钥无法读取", ex);
        }

        try
        {
            var parsed = ParseKeyFile(fileBytes);
            try
            {
                var keyBytes = UnwrapKeyData(parsed);
                if (keyBytes == null)
                    throw new CookieVaultException(CookieVaultError.KeyDecryptFailed,
                        "Cookie 加密密钥无法由 DPAPI 或 HWID 解密");
                return new CookieKey(keyBytes, parsed.KeyId);
            }
            finally
            {
                parsed.Clear();
            }
        }
        finally
        {
            Zero(fileBytes);
        }
    }

    private static ParsedKeyFile ParseKeyFile(byte[] fileBytes)
    {
        if (fileBytes.Length < KeyHeaderSize
            || !fileBytes.AsSpan(0, KeyMagic.Length).SequenceEqual(KeyMagic))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

        byte version = fileBytes[KeyMagic.Length];
        var keyId = fileBytes.AsSpan(KeyMagic.Length + 1, KeyIdSize).ToArray();

        if (version == LegacyKeyFileVersion)
        {
            if (fileBytes.Length <= KeyHeaderSize)
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
            return new ParsedKeyFile
            {
                Version = version,
                KeyId = keyId,
                DpapiBlob = fileBytes.AsSpan(KeyHeaderSize).ToArray()
            };
        }

        if (version != KeyFileVersion || fileBytes.Length < KeyHeaderSize + 1)
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

        int offset = KeyHeaderSize;
        byte flags = fileBytes[offset++];
        if ((flags & ~(KeyFlagDpapi | KeyFlagHwid)) != 0)
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥保护标志无效");

        var parsed = new ParsedKeyFile { Version = version, KeyId = keyId };
        try
        {
            if ((flags & KeyFlagDpapi) != 0)
            {
                if (fileBytes.Length < offset + LengthSize)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                int length = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(offset, LengthSize));
                offset += LengthSize;
                if (length <= 0 || length > fileBytes.Length - offset)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.DpapiBlob = fileBytes.AsSpan(offset, length).ToArray();
                offset += length;
            }

            if ((flags & KeyFlagHwid) != 0)
            {
                if (fileBytes.Length < offset + NonceSize + TagSize + LengthSize)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.HwidNonce = fileBytes.AsSpan(offset, NonceSize).ToArray();
                offset += NonceSize;
                parsed.HwidTag = fileBytes.AsSpan(offset, TagSize).ToArray();
                offset += TagSize;
                int length = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(offset, LengthSize));
                offset += LengthSize;
                if (length != KeySize || fileBytes.Length < offset + length)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.HwidCipher = fileBytes.AsSpan(offset, length).ToArray();
                offset += length;
            }

            if (offset != fileBytes.Length
                || (parsed.DpapiBlob == null && parsed.HwidCipher == null))
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

            return parsed;
        }
        catch
        {
            parsed.Clear();
            throw;
        }
    }

    private static byte[]? UnwrapKeyData(ParsedKeyFile parsed)
    {
        if (parsed.DpapiBlob != null && TryUnprotectDpapi(parsed.DpapiBlob, out var dpapiKey))
            return dpapiKey;

        if (parsed.HwidNonce != null && parsed.HwidTag != null && parsed.HwidCipher != null)
        {
            try
            {
                return UnwrapHwid(parsed);
            }
            catch (Exception ex) when (ex is CryptographicException or CookieVaultException)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] Failed to unprotect HWID key: {ex.Message}");
            }
        }

        return null;
    }

    private static bool TryUnprotectDpapi(byte[] blob, out byte[] keyBytes)
    {
        try
        {
            var key = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            if (key.Length == KeySize)
            {
                keyBytes = key;
                return true;
            }

            Zero(key);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            DebugLog.Info("CookieVault", $"[CookieVault] DPAPI key unprotect failed, trying HWID: {ex.Message}");
        }

        keyBytes = Array.Empty<byte>();
        return false;
    }

    private static byte[] BuildKeyFileV2(byte[] keyId, byte[] keyBytes, bool allowDpapi)
    {
        byte[]? dpapiBlob = null;
        byte[]? hwidNonce = null;
        byte[]? hwidTag = null;
        byte[]? hwidCipher = null;
        try
        {
            if (allowDpapi)
            {
                try
                {
                    dpapiBlob = ProtectedData.Protect(keyBytes, Entropy, DataProtectionScope.CurrentUser);
                }
                catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
                {
                    DebugLog.Info("CookieVault",
                        $"[CookieVault] DPAPI protection failed, enabling HWID protection: {ex.Message}");
                }
            }

            try
            {
                var wrapped = WrapHwid(keyId, keyBytes);
                hwidNonce = wrapped.Nonce;
                hwidTag = wrapped.Tag;
                hwidCipher = wrapped.Ciphertext;
            }
            catch (Exception ex) when (ex is CookieVaultException or CryptographicException)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] HWID protection unavailable: {ex.Message}");
            }

            byte flags = 0;
            if (dpapiBlob != null)
                flags |= KeyFlagDpapi;
            if (hwidCipher != null)
                flags |= KeyFlagHwid;
            if (flags == 0)
                throw new CookieVaultException(CookieVaultError.KeyInvalid,
                    "DPAPI 与 HWID 均不可用，无法保护 Cookie 密钥");

            int size = KeyHeaderSize + 1;
            if (dpapiBlob != null)
                size += LengthSize + dpapiBlob.Length;
            if (hwidCipher != null)
                size += NonceSize + TagSize + LengthSize + hwidCipher.Length;

            var payload = new byte[size];
            KeyMagic.CopyTo(payload, 0);
            payload[KeyMagic.Length] = KeyFileVersion;
            keyId.CopyTo(payload, KeyMagic.Length + 1);

            int offset = KeyHeaderSize;
            payload[offset++] = flags;

            if (dpapiBlob != null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, LengthSize), dpapiBlob.Length);
                offset += LengthSize;
                dpapiBlob.CopyTo(payload, offset);
                offset += dpapiBlob.Length;
            }

            if (hwidCipher != null)
            {
                hwidNonce!.CopyTo(payload, offset);
                offset += NonceSize;
                hwidTag!.CopyTo(payload, offset);
                offset += TagSize;
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, LengthSize), hwidCipher.Length);
                offset += LengthSize;
                hwidCipher.CopyTo(payload, offset);
            }

            return payload;
        }
        finally
        {
            Zero(dpapiBlob);
            Zero(hwidNonce);
            Zero(hwidTag);
            Zero(hwidCipher);
        }
    }

    private static (byte[] Nonce, byte[] Tag, byte[] Ciphertext) WrapHwid(byte[] keyId, byte[] keyBytes)
    {
        var hwidKey = DeriveHwidKey(keyId);
        var associatedData = BuildHwidAssociatedData(keyId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[keyBytes.Length];
        try
        {
            using var aes = new AesGcm(hwidKey, TagSize);
            aes.Encrypt(nonce, keyBytes, ciphertext, tag, associatedData);
            return (nonce, tag, ciphertext);
        }
        catch
        {
            Zero(ciphertext);
            throw;
        }
        finally
        {
            Zero(hwidKey);
            Zero(associatedData);
        }
    }

    private static byte[] UnwrapHwid(ParsedKeyFile parsed)
    {
        var hwidKey = DeriveHwidKey(parsed.KeyId);
        var associatedData = BuildHwidAssociatedData(parsed.KeyId);
        var plaintext = new byte[parsed.HwidCipher!.Length];
        try
        {
            try
            {
                using var aes = new AesGcm(hwidKey, TagSize);
                aes.Decrypt(parsed.HwidNonce!, parsed.HwidCipher, parsed.HwidTag!, plaintext, associatedData);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "HWID 密钥完整性校验失败", ex);
            }

            if (plaintext.Length != KeySize)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "HWID 解保护的 Cookie 密钥长度无效");
            }

            return plaintext;
        }
        finally
        {
            Zero(hwidKey);
            Zero(associatedData);
        }
    }

    private static byte[] CreateMachineMagic()
    {
        string identity = TryGetSystemVolumeSerial(out uint serial)
            ? serial.ToString("X8")
            : Environment.MachineName;
        var source = Encoding.UTF8.GetBytes($"FufuLauncher.CookieVault.Magic.v1|{identity}");
        try
        {
            var hash = SHA256.HashData(source);
            try
            {
                return hash.AsSpan(0, MachineMagicSize).ToArray();
            }
            finally
            {
                Zero(hash);
            }
        }
        finally
        {
            Zero(source);
        }
    }

    private static byte[] DeriveHwidKey(byte[] keyId)
    {
        if (!TryGetSystemVolumeSerial(out uint serial))
            throw new CookieVaultException(CookieVaultError.HwidUnavailable, "无法读取 C 盘序列号");

        var password = Encoding.UTF8.GetBytes($"FufuLauncher.HWID.v1|{serial:X8}");
        var salt = new byte[HwidSalt.Length + keyId.Length];
        HwidSalt.CopyTo(salt, 0);
        keyId.CopyTo(salt, HwidSalt.Length);
        try
        {
            using var derive = new Rfc2898DeriveBytes(password, salt, HwidIterations, HashAlgorithmName.SHA256);
            return derive.GetBytes(KeySize);
        }
        finally
        {
            Zero(password);
            Zero(salt);
        }
    }

    private static bool TryGetSystemVolumeSerial(out uint serial)
    {
        if (GetVolumeInformation("C:\\", IntPtr.Zero, 0, out serial, out _, out _, IntPtr.Zero, 0))
            return true;

        string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrEmpty(systemRoot)
            && !string.Equals(systemRoot, "C:\\", StringComparison.OrdinalIgnoreCase)
            && GetVolumeInformation(systemRoot, IntPtr.Zero, 0, out serial, out _, out _, IntPtr.Zero, 0))
            return true;

        serial = 0;
        return false;
    }

    private static byte[] BuildHwidAssociatedData(byte[] keyId)
    {
        return Encoding.UTF8.GetBytes(string.Join(",",
            FormatName,
            "HWID.v1",
            Convert.ToBase64String(keyId)));
    }

    private static void QuarantineKeyFile(string keyPath)
    {
        try
        {
            if (!File.Exists(keyPath))
                return;

            string quarantinePath = keyPath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Move(keyPath, quarantinePath);
            DebugLog.Info("CookieVault",
                $"[CookieVault] Quarantined undecryptable Cookie encryption key: {Path.GetFileName(quarantinePath)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("CookieVault",
                $"[CookieVault] Failed to quarantine undecryptable Cookie encryption key: {ex.Message}");
            try
            {
                if (File.Exists(keyPath))
                    File.Delete(keyPath);
            }
            catch
            {
            }
        }
    }

    private static void WriteKeyFile(string keyPath, byte[] payload, bool overwrite)
    {
        string tempPath = keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }

            File.Move(tempPath, keyPath, overwrite);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static byte[] BuildBinaryAssociatedData(string fileName, byte[] keyId)
    {
        string value = string.Join(",",
            BinaryFormatName,
            BinaryFormatVersion.ToString(),
            AlgorithmName,
            KeyProtectionName,
            Convert.ToBase64String(keyId),
            fileName);
        return Encoding.UTF8.GetBytes(value);
    }

    private static byte[] BuildAssociatedData(string fileName, byte[] keyId, int version, string keyProtection)
    {
        string value = string.Join(",",
            FormatName,
            version.ToString(),
            AlgorithmName,
            keyProtection,
            Convert.ToBase64String(keyId),
            fileName);
        return Encoding.UTF8.GetBytes(value);
    }

    private static string GetKeyPath(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥目录无效");
        return Path.Combine(dataDir, "secrets", "cookie.key");
    }

    private static void Zero(byte[]? data)
    {
        if (data != null)
            CryptographicOperations.ZeroMemory(data);
    }

    private readonly record struct CookieKey(byte[] KeyBytes, byte[] KeyId);

    private sealed class ParsedKeyFile
    {
        public byte Version
        {
            get;
            init;
        }

        public byte[] KeyId
        {
            get;
            init;
        } = Array.Empty<byte>();

        public byte[]? DpapiBlob
        {
            get;
            set;
        }

        public byte[]? HwidNonce
        {
            get;
            set;
        }

        public byte[]? HwidTag
        {
            get;
            set;
        }

        public byte[]? HwidCipher
        {
            get;
            set;
        }

        public void Clear()
        {
            if (DpapiBlob != null)
                CryptographicOperations.ZeroMemory(DpapiBlob);
            if (HwidNonce != null)
                CryptographicOperations.ZeroMemory(HwidNonce);
            if (HwidTag != null)
                CryptographicOperations.ZeroMemory(HwidTag);
            if (HwidCipher != null)
                CryptographicOperations.ZeroMemory(HwidCipher);
        }
    }
}

internal sealed class CookieFileEnvelope
{
    [JsonPropertyName("format")]
    public string Format
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("version")]
    public int Version
    {
        get;
        set;
    }

    [JsonPropertyName("algorithm")]
    public string Algorithm
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("keyProtection")]
    public string KeyProtection
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("keyId")]
    public string KeyId
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ciphertext")]
    public string Ciphertext
    {
        get;
        set;
    } = string.Empty;
}

internal sealed class CookieVaultException : Exception
{
    public CookieVaultException(CookieVaultError error, string message)
        : base(message)
    {
        Error = error;
    }

    public CookieVaultException(CookieVaultError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public CookieVaultError Error
    {
        get;
    }
}