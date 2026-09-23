using System.Reflection;

namespace SafeSpend.Web;

public static class BuildInformation
{
    private static readonly string InformationalVersion =
        typeof(BuildInformation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    public static string DisplayVersion =>
        InformationalVersion.StartsWith('v')
            ? InformationalVersion
            : $"v{InformationalVersion}";
}
