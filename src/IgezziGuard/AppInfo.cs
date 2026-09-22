using System.Reflection;
namespace IgezziGuard;
internal static class AppInfo
{
    internal static string Version => typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
