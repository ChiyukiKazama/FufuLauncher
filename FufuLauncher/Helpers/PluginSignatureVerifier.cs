/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace FufuLauncher.Helpers;

public sealed class PluginSignatureDetail
{
    public PluginSignatureDetail(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label
    {
        get;
    }

    public string Value
    {
        get;
    }
}

public sealed class PluginSignatureInfo
{
    public string DisplayName
    {
        get;
        set;
    } = string.Empty;

    public string FilePath
    {
        get;
        set;
    } = string.Empty;

    public bool IsSigned
    {
        get;
        set;
    }

    public bool IsTrusted
    {
        get;
        set;
    }

    public string TrustStatus
    {
        get;
        set;
    } = string.Empty;

    public List<PluginSignatureDetail> Details
    {
        get;
    } = new();
}

public static class PluginSignatureVerifier
{
    private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    private const uint TRUST_E_BAD_DIGEST = 0x80096010;
    private const uint TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004;
    private const uint CERT_E_EXPIRED = 0x800B0101;
    private const uint CERT_E_UNTRUSTEDTESTROOT = 0x800B0102;
    private const uint CERT_E_CHAINING = 0x800B010A;
    private const uint CERT_E_REVOKED = 0x800B010C;
    private const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;
    private const uint CERT_E_WRONG_USAGE = 0x800B0110;

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr data);

    public static IReadOnlyList<PluginSignatureInfo> VerifyInstalledPlugins()
    {
        var results = new List<PluginSignatureInfo>();

        try
        {
            string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir)) return results;

            foreach (var file in EnumeratePluginFiles(pluginsDir))
            {
                results.Add(VerifyFile(file));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier", $"[Signature] Failed to scan plugin directory: {ex.Message}");
        }

        return results
            .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static PluginSignatureInfo VerifyFile(string filePath)
    {
        var info = new PluginSignatureInfo
        {
            FilePath = filePath
        };

        string folderName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? string.Empty);
        info.DisplayName = string.IsNullOrEmpty(folderName) ? Path.GetFileName(filePath) : folderName;

        uint trustResult = VerifyTrust(filePath);
        var certificate = TryReadCertificate(filePath);

        info.IsSigned = certificate != null || trustResult != TRUST_E_NOSIGNATURE;
        info.IsTrusted = info.IsSigned && trustResult == 0;
        info.TrustStatus = DescribeTrust(trustResult, info.IsSigned);

        info.Details.Add(new PluginSignatureDetail("PluginSignature_File".GetLocalized(), filePath));

        if (certificate != null)
        {
            info.Details.Add(new PluginSignatureDetail("PluginSignature_Signer".GetLocalized(), certificate.Subject));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_Issuer".GetLocalized(), certificate.Issuer));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_SerialNumber".GetLocalized(),
                certificate.SerialNumber));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_Thumbprint".GetLocalized(),
                certificate.Thumbprint));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_ValidFrom".GetLocalized(),
                certificate.NotBefore.ToString("yyyy-MM-dd HH:mm:ss")));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_ValidTo".GetLocalized(),
                certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss")));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_SignatureAlgorithm".GetLocalized(),
                GetAlgorithmName(certificate)));
            info.Details.Add(new PluginSignatureDetail("PluginSignature_PublicKey".GetLocalized(),
                GetPublicKeyName(certificate)));
            certificate.Dispose();
        }

        info.Details.Add(new PluginSignatureDetail("PluginSignature_TrustStatus".GetLocalized(), info.TrustStatus));

        return info;
    }

    private static IEnumerable<string> EnumeratePluginFiles(string pluginsDir)
    {
        try
        {
            return Directory.GetFiles(pluginsDir, "*", SearchOption.AllDirectories)
                .Where(IsPluginFile)
                .ToList();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier", $"[Signature] Failed to enumerate plugin files: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static bool IsPluginFile(string path)
    {
        string name = Path.GetFileName(path);

        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static X509Certificate2? TryReadCertificate(string filePath)
    {
        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(filePath);
            return new X509Certificate2(certificate);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier",
                $"[Signature] Failed to read signing certificate {filePath}: {ex.Message}");
            return null;
        }
    }

    private static uint VerifyTrust(string filePath)
    {
        IntPtr filePathPtr = IntPtr.Zero;
        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr trustDataPtr = IntPtr.Zero;

        try
        {
            filePathPtr = Marshal.StringToHGlobalUni(filePath);

            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPtr,
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };

            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = WTD_UI_NONE,
                RevocationChecks = WTD_REVOKE_NONE,
                UnionChoice = WTD_CHOICE_FILE,
                FileInfo = fileInfoPtr,
                StateAction = WTD_STATEACTION_IGNORE,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                UiContext = 0
            };

            trustDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPtr, false);

            return WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, trustDataPtr);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier",
                $"[Signature] Failed to verify trust status {filePath}: {ex.Message}");
            return TRUST_E_NOSIGNATURE;
        }
        finally
        {
            if (trustDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(trustDataPtr);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            if (filePathPtr != IntPtr.Zero) Marshal.FreeHGlobal(filePathPtr);
        }
    }

    private static string DescribeTrust(uint trustResult, bool isSigned)
    {
        if (trustResult == 0)
        {
            return "PluginSignature_TrustValid".GetLocalized();
        }

        if (trustResult == TRUST_E_NOSIGNATURE)
        {
            return "PluginSignature_TrustNotSigned".GetLocalized();
        }

        string? key = trustResult switch
        {
            TRUST_E_BAD_DIGEST => "PluginSignature_TrustBadDigest",
            TRUST_E_SUBJECT_NOT_TRUSTED => "PluginSignature_TrustSubjectNotTrusted",
            CERT_E_EXPIRED => "PluginSignature_TrustExpired",
            CERT_E_REVOKED => "PluginSignature_TrustRevoked",
            CERT_E_UNTRUSTEDROOT => "PluginSignature_TrustUntrustedRoot",
            CERT_E_UNTRUSTEDTESTROOT => "PluginSignature_TrustUntrustedRoot",
            CERT_E_CHAINING => "PluginSignature_TrustUntrustedRoot",
            CERT_E_WRONG_USAGE => "PluginSignature_TrustWrongUsage",
            _ => null
        };

        if (key != null) return key.GetLocalized();

        return isSigned
            ? string.Format("PluginSignature_TrustUnknown".GetLocalized(), trustResult.ToString("X8"))
            : "PluginSignature_TrustNotSigned".GetLocalized();
    }

    private static string GetAlgorithmName(X509Certificate2 certificate)
    {
        try
        {
            return certificate.SignatureAlgorithm.FriendlyName ?? certificate.SignatureAlgorithm.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier", $"[Signature] Failed to read signature algorithm: {ex.Message}");
            return string.Empty;
        }
    }

    private static string GetPublicKeyName(X509Certificate2 certificate)
    {
        try
        {
            return certificate.PublicKey.Oid.FriendlyName ?? certificate.PublicKey.Oid.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSignatureVerifier", $"[Signature] Failed to read public key info: {ex.Message}");
            return string.Empty;
        }
    }
}