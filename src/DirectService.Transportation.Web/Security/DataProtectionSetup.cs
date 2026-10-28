using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace DirectService.Transportation.Web.Security;

/// <summary>
/// The Data Protection key ring (DT-221): it signs the auth cookies and antiforgery tokens and encrypts stored credentials, so it must
/// survive restarts and updates — otherwise every user is signed out and every open form is refused after a deployment.
/// <list type="bullet">
/// <item>One stable application name (never the default, which depends on the install path).</item>
/// <item>Keys persisted in <c>DataProtection:KeysPath</c>; a relative path is anchored to the content root, not to the process working
/// directory (which differs between <c>dotnet run</c>, Visual Studio, a Windows service and a container).</item>
/// <item>Protected at rest: with the certificate in <c>DataProtection:CertificatePath</c> (+ <c>CertificatePassword</c>) when set,
/// otherwise DPAPI on Windows. Production on another system refuses to start without a certificate unless
/// <c>DataProtection:AllowUnprotectedKeys</c> says the volume is encrypted by the host.</item>
/// </list>
/// </summary>
internal static class DataProtectionSetup
{
    /// <summary>Isolates the key ring (platform design §e, E-22): a pinned literal forever — changing it signs everyone out and makes every
    /// value protected with Data Protection unreadable. It is not the assembly or product name and never follows a rename.</summary>
    public const string ApplicationName = "DirectService.Transportation";

    /// <summary>Default key folder, relative to the content root (E-24).</summary>
    public const string DefaultKeysPath = ".local/keys";

    public static void Add(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var dp = services.AddDataProtection().SetApplicationName(ApplicationName).PersistKeysToFileSystem(KeysFolder(config, env));
        if (config["DataProtection:CertificatePath"] is { Length: > 0 } path)
        {
            // The same certificate decrypts the keys again (without UnprotectKeysWithAnyCertificate it would be looked up in the machine's
            // certificate store, where a mounted .pfx is not).
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, config["DataProtection:CertificatePassword"],
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
            dp.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        }
        else if (OperatingSystem.IsWindows())
            dp.ProtectKeysWithDpapi();
        else if (env.IsProduction() && !config.GetValue("DataProtection:AllowUnprotectedKeys", false))
            throw new InvalidOperationException("The Data Protection keys would be stored unencrypted: set DataProtection:CertificatePath "
                + "(and DataProtection:CertificatePassword), or DataProtection:AllowUnprotectedKeys=true when the key volume is encrypted by the host.");
    }

    /// <summary>The key folder: <c>DataProtection:KeysPath</c> (default <c>.local/keys</c>), relative paths under the content root.</summary>
    public static DirectoryInfo KeysFolder(IConfiguration config, IHostEnvironment env) =>
        new(Path.GetFullPath(config["DataProtection:KeysPath"] is { Length: > 0 } p ? p : DefaultKeysPath, env.ContentRootPath));
}
