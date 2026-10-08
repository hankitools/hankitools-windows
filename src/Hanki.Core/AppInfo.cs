using System.Reflection;
namespace IgezziGuard;
internal static class AppInfo
{
    /// <summary>Brand line, shared with hanki.tools. Shown in capitals with a leading "+" as an eyebrow.</summary>
    internal const string Tagline = "A little sisu for your PC";
    internal const string TaglineMeaning = "Sisu is Finnish for grit: the quiet determination to keep going.";
    internal static string Version => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
