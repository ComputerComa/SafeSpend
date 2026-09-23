using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SafeSpend.Web.Services.Plaid;

public interface ILegacyPlaidAccessTokenProtector
{
    int CandidateCount { get; }

    int KeyDirectoryCount { get; }

    bool TryUnprotect(string protectedAccessToken, out string accessToken);
}

public sealed class LegacyPlaidAccessTokenProtector :
    ILegacyPlaidAccessTokenProtector,
    IDisposable
{
    private readonly IReadOnlyList<IDataProtectionProvider> _providers;
    private readonly IReadOnlyList<IDataProtector> _protectors;

    public int CandidateCount => _protectors.Count;

    public int KeyDirectoryCount { get; }

    public LegacyPlaidAccessTokenProtector(
        string keyDirectory,
        IEnumerable<string> applicationNames)
        : this([keyDirectory], applicationNames)
    {
    }

    public LegacyPlaidAccessTokenProtector(
        IEnumerable<string> keyDirectories,
        IEnumerable<string> applicationNames)
    {
        ArgumentNullException.ThrowIfNull(keyDirectories);
        ArgumentNullException.ThrowIfNull(applicationNames);

        var names = applicationNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(GetApplicationNameVariants)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var directories = keyDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var providers = new List<IDataProtectionProvider>();
        var protectors = new List<IDataProtector>();
        foreach (var directory in directories)
        {
            var unisolatedProvider = DataProtectionProvider.Create(
                new DirectoryInfo(directory));
            providers.Add(unisolatedProvider);
            protectors.Add(unisolatedProvider.CreateProtector(
                PlaidConnectionStore.AccessTokenPurpose));

            foreach (var name in names)
            {
                var provider = DataProtectionProvider.Create(
                    new DirectoryInfo(directory),
                    builder => builder.SetApplicationName(name));
                providers.Add(provider);
                protectors.Add(provider.CreateProtector(
                    PlaidConnectionStore.AccessTokenPurpose));
            }
        }

        _providers = providers;
        _protectors = protectors;
        KeyDirectoryCount = directories.Length;
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

    public static IReadOnlyList<string> LoadKeyDirectories(
        string primaryKeyDirectory,
        string contentRootPath,
        IEnumerable<string>? configuredDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var directories = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetFullPath(primaryKeyDirectory)
        };
        if (configuredDirectories is not null)
        {
            foreach (var configuredDirectory in configuredDirectories
                         .Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                directories.Add(Path.GetFullPath(configuredDirectory));
            }
        }

        try
        {
            AddKeyDirectories(
                directories,
                new DirectoryInfo(primaryKeyDirectory));

            foreach (var release in GetRetainedReleaseDirectories(
                         contentRootPath))
            {
                AddKeyDirectories(directories, release);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The primary persistent key directory remains available.
        }

        return directories.ToArray();
    }

    private static void AddKeyDirectories(
        ISet<string> directories,
        DirectoryInfo root)
    {
        if (!root.Exists)
        {
            return;
        }

        foreach (var keyFile in root.EnumerateFiles(
                     "key-*.xml",
                     SearchOption.AllDirectories))
        {
            if (keyFile.Directory is not null)
            {
                directories.Add(keyFile.Directory.FullName);
            }
        }
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

            foreach (var release in GetRetainedReleaseDirectories(
                         contentRootPath))
            {
                names.Add(release.FullName);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Explicitly configured names and the compatibility file remain
            // available when release-directory discovery is unavailable.
        }
    }

    private static IReadOnlyList<DirectoryInfo>
        GetRetainedReleaseDirectories(string contentRootPath)
    {
        var contentRoot = new DirectoryInfo(contentRootPath);
        var resolvedRoot = contentRoot.ResolveLinkTarget(
            returnFinalTarget: true) as DirectoryInfo;
        var releaseRoots = new List<DirectoryInfo>();

        AddReleaseRoot(releaseRoots, contentRoot.Parent);
        AddReleaseRoot(releaseRoots, resolvedRoot?.Parent);

        if (contentRoot.Parent is not null)
        {
            var siblingReleases = new DirectoryInfo(Path.Combine(
                contentRoot.Parent.FullName,
                "releases"));
            if (siblingReleases.Exists)
            {
                releaseRoots.Add(siblingReleases);
            }
        }

        return releaseRoots
            .DistinctBy(directory => directory.FullName)
            .SelectMany(directory => directory.EnumerateDirectories())
            .DistinctBy(directory => directory.FullName)
            .ToArray();
    }

    private static void AddReleaseRoot(
        ICollection<DirectoryInfo> releaseRoots,
        DirectoryInfo? candidate)
    {
        if (candidate is not null &&
            string.Equals(
                candidate.Name,
                "releases",
                StringComparison.OrdinalIgnoreCase))
        {
            releaseRoots.Add(candidate);
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
