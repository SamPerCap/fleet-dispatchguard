using System.Security.Cryptography;
using System.Xml.Linq;
using DispatchGuard.API.Perimeter;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace DispatchGuard.API.Session;

/// <summary>
/// The key that signs the session cookies, from the configuration: the same in every instance, without expiry and without a database
/// to read it from, which would wake up with each start (SPEC 0086, ADR 0025). To change it is to end every session.
/// </summary>
public sealed class SessionKey
{
    private const string Setting = "Session:Key";

    private const int SmallestLength = 32;

    /// <summary>By default it depends on the folder, and two installations would not read each other's cookies.</summary>
    private const string ApplicationName = "lof";

    /// <summary>Any instant before the first deployment.</summary>
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly byte[] _secret;

    private SessionKey(byte[] secret) => _secret = secret;

    /// <exception cref="InvalidOperationException">There is no proper key outside a development machine.</exception>
    public static SessionKey From(IConfiguration configuration, IHostEnvironment environment)
    {
        var text = configuration[Setting] ?? string.Empty;
        if (text.Length == 0 && environment.IsDevelopmentOrTesting())
        {
            return new SessionKey(RandomNumberGenerator.GetBytes(SmallestLength));
        }

        var secret = new byte[text.Length];
        return Convert.TryFromBase64String(text, secret, out var length) && length >= SmallestLength
            ? new SessionKey(secret[..length])
            : throw new InvalidOperationException($"{Setting} must be at least {SmallestLength} random bytes in base64");
    }

    public void AddTo(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName(ApplicationName).DisableAutomaticKeyGeneration();
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = new OnlyThisKey(ToXml()));
    }

    /// <summary>As the key manager of the framework would have stored it.</summary>
    private XElement ToXml()
    {
        using var secret = new Secret(_secret);
        var descriptor = new AuthenticatedEncryptorDescriptor(new AuthenticatedEncryptorConfiguration(), secret).ExportToXml();
        return new XElement(
            "key",
            new XAttribute("id", new Guid(SHA256.HashData(_secret).AsSpan(0, 16))),
            new XAttribute("version", 1),
            new XElement("creationDate", Start),
            new XElement("activationDate", Start),
            new XElement("expirationDate", DateTimeOffset.MaxValue),
            new XElement(
                "descriptor",
                new XAttribute("deserializerType", descriptor.DeserializerType.AssemblyQualifiedName!),
                descriptor.SerializedDescriptorElement));
    }

    private sealed class OnlyThisKey(XElement key) : IXmlRepository
    {
        public IReadOnlyCollection<XElement> GetAllElements() => [new(key)];

        public void StoreElement(XElement element, string friendlyName) => throw new NotSupportedException();
    }
}
