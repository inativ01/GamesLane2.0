namespace GamesLane.Server.Auth;

/// <summary>
/// The Android app's settings (the <c>Android</c> configuration section): its package name, and the SHA-256
/// fingerprints of the certificates that sign it (<c>CertificateFingerprints</c>, separated by commas: Google Play's app
/// signing key, the upload key, and a debug key if wanted), e.g. <c>AB:CD:...</c>, as Play Console shows them.
/// </summary>
public record AndroidSettings(string PackageName, IReadOnlyList<string> CertificateFingerprints)
{
    /// <summary>
    /// Reads the settings.
    /// </summary>
    public static AndroidSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Android");
        var fingerprints = (section["CertificateFingerprints"] ?? "")
            .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToUpperInvariant())
            .Distinct()
            .ToList();
        return new AndroidSettings(section["PackageName"] ?? "", fingerprints);
    }

    /// <summary>
    /// The site's <c>/.well-known/assetlinks.json</c> (Digital Asset Links): join links (<c>/join/...</c>) open the
    /// Android app, and the app's sign-in tab may end at an address on the site. Empty until the package and its
    /// certificates are set.
    /// </summary>
    public object[] AssetLinks() =>
        PackageName.Length == 0 || CertificateFingerprints.Count == 0
            ? []
            :
            [
                new
                {
                    relation = new[] { "delegate_permission/common.handle_all_urls", "delegate_permission/common.get_login_creds" },
                    target = new
                    {
                        @namespace = "android_app",
                        package_name = PackageName,
                        sha256_cert_fingerprints = CertificateFingerprints,
                    },
                },
            ];
}
