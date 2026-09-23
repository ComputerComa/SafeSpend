using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SafeSpend.Web.Services.Plaid;

public interface ILegacyPlaidAccessTokenProtector
{
    bool TryUnprotect(string protectedAccessToken, out string accessToken);
}

public sealed class LegacyPlaidAccessTokenProtector :
    ILegacyPlaidAccessTokenProtector,
    IDisposable
{
    private readonly IReadOnlyList<IDataProtectionProvider> _providers;
    private readonly IReadOnlyList<IDataProtector> _protectors;

    public LegacyPlaidAccessTokenProtector(
        string keyDirectory,
        IEnumerable<string> applicationNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyDirectory);
        ArgumentNullException.ThrowIfNull(applicationNames);

        var names = applicationNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(GetApplicationNameVariants)
            .Where(name => !string.Equals(
                name,
                PlaidConnectionStore.ApplicationName,
                StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var providers = new List<IDataProtectionProvider>(names.Length);
        var protectors = new List<IDataProtector>(names.Length);
        foreach (var name in names)
        {
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(keyDirectory),
                builder => builder.SetApplicationName(name));
            providers.Add(provider);
            protectors.Add(provider.CreateProtector(
                PlaidConnectionStore.AccessTokenPurpose));
        }

        _providers = providers;
        _protectors = protectors;
    }

    public bool TryUnprotect(
        string protectedAccessToken,
        out string accessToken)
    {
        foreach (var protector in _protectors)
        {
            try
            {
                accessToken = protector.Unprotect(protectedAccessToken);
                return true;
            }
            catch (Exception exception) when (
                exception is CryptographicException or ArgumentException)
            {
                // Try the next known application discriminator.
            }
        }

        accessToken = string.Empty;
        return false;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public static IReadOnlyList<string> LoadApplicationNames(
        string filePath,
        IEnumerable<string> configuredNames,
        string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(configuredNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var names = new List<string>(configuredNames);
        if (File.Exists(filePath))
        {
            names.AddRange(File.ReadLines(filePath));
        }

        names.Add(contentRootPath);
        AddRetainedReleasePaths(names, contentRootPath);

        return names;
    }

    private static void AddRetainedReleasePaths(
        ICollection<string> names,
        string contentRootPath)
    {
        try
        {
            var contentRoot = new DirectoryInfo(contentRootPath);
            var resolvedRoot = contentRoot.ResolveLinkTarget(
                returnFinalTarget: true) as DirectoryInfo;
            if (resolvedRoot is not null)
            {
                names.Add(resolvedRoot.FullName);
            }

            var releaseDirectories = new List<DirectoryInfo>();
            var contentRootParent = contentRoot.Parent;
            if (string.Equals(
                    contentRootParent?.Name,
                    "releases",
                    StringComparison.OrdinalIgnoreCase))
            {
                releaseDirectories.Add(contentRootParent!);
            }

            var siblingReleases = contentRootParent is null
                ? null
                : new DirectoryInfo(Path.Combine(
                    contentRootParent.FullName,
                    "releases"));
            if (siblingReleases?.Exists == true)
            {
                releaseDirectories.Add(siblingReleases);
            }

            if (resolvedRoot?.Parent is not null &&
                string.Equals(
                    resolvedRoot.Parent.Name,
                    "releases",
                    StringComparison.OrdinalIgnoreCase))
            {
                releaseDirectories.Add(resolvedRoot.Parent);
            }

            foreach (var releases in releaseDirectories
                         .DistinctBy(directory => directory.FullName))
            {
                foreach (var release in releases.EnumerateDirectories())
                {
                    names.Add(release.FullName);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Explicitly configured names and the compatibility file remain
            // available when release-directory discovery is unavailable.
        }
    }

    private static IEnumerable<string> GetApplicationNameVariants(
        string applicationName)
    {
        var trimmedName = applicationName.Trim();
        yield return trimmedName;

        if (trimmedName.EndsWith(
                Path.DirectorySeparatorChar.ToString(),
                StringComparison.Ordinal))
        {
            yield return trimmedName.TrimEnd(Path.DirectorySeparatorChar);
        }
        else
        {
            yield return trimmedName + Path.DirectorySeparatorChar;
        }
    }
}
