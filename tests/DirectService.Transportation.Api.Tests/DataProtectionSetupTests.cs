using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DirectService.Transportation.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// The key ring set-up (DT-221): keys persisted under the content root with a stable application name, protected at rest by the configured
/// certificate (and readable again by a new process), and Production on a system without DPAPI refuses unprotected keys.
/// </summary>
public sealed class DataProtectionSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dst-dp-" + Guid.NewGuid().ToString("N"));

    public DataProtectionSetupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private HostingEnvironment Env(string name = "Development") => new() { EnvironmentName = name, ContentRootPath = _root, ApplicationName = "Any.Install.Folder" };

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static IDataProtector Protector(IConfiguration config, IHostEnvironment env)
    {
        var services = new ServiceCollection();
        DataProtectionSetup.Add(services, config, env);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
    }

    [Fact]
    public void A_relative_key_folder_is_under_the_content_root_not_the_working_directory()
    {
        Assert.Equal(Path.Combine(_root, ".local", "keys"), DataProtectionSetup.KeysFolder(Config(), Env()).FullName);
        Assert.Equal(Path.Combine(_root, "k"), DataProtectionSetup.KeysFolder(Config(("DataProtection:KeysPath", "k")), Env()).FullName);
    }

    [Fact]
    public void Keys_protected_with_the_certificate_are_read_back_by_a_new_process()
    {
        using var rsa = RSA.Create(2048);
        using var cert = new CertificateRequest("CN=dst-keys-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfx = Path.Combine(_root, "keys.pfx");
        File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pkcs12, "pfx-pass"));
        var config = Config(("DataProtection:KeysPath", "ring"), ("DataProtection:CertificatePath", pfx), ("DataProtection:CertificatePassword", "pfx-pass"));

        var secret = Protector(config, Env("Production")).Protect("signed-in");
        var keyFile = Directory.GetFiles(Path.Combine(_root, "ring"), "key-*.xml").Single();
        var xml = File.ReadAllText(keyFile);
        Assert.Contains("EncryptedData", xml, StringComparison.Ordinal);   // the master key is encrypted at rest
        Assert.DoesNotContain("<value>", xml, StringComparison.Ordinal);

        Assert.Equal("signed-in", Protector(config, Env("Production")).Unprotect(secret));   // "restart": new container, same folder
    }

    [Fact]
    public void The_application_name_does_not_depend_on_the_install_folder()
    {
        var config = Config(("DataProtection:KeysPath", "ring"));
        var secret = Protector(config, Env()).Protect("x");
        var moved = new HostingEnvironment { EnvironmentName = "Development", ContentRootPath = _root, ApplicationName = "Other.Folder" };
        Assert.Equal("x", Protector(config, moved).Unprotect(secret));
    }

    [Fact]
    public void Production_without_DPAPI_or_a_certificate_refuses_unprotected_keys()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows protects the keys with DPAPI.");
        var e = Assert.Throws<InvalidOperationException>(() => Protector(Config(("DataProtection:KeysPath", "ring")), Env("Production")));
        Assert.Contains("DataProtection:CertificatePath", e.Message, StringComparison.Ordinal);
        Protector(Config(("DataProtection:KeysPath", "ring"), ("DataProtection:AllowUnprotectedKeys", "true")), Env("Production"));   // explicit opt-out
    }
}
