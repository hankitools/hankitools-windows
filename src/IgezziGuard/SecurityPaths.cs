namespace IgezziGuard;

internal static class SecurityPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IgezziGuard");

    public static readonly string Quarantine = Path.Combine(Root, "Quarantine");
    public static readonly string History = Path.Combine(Root, "history.json");
    public static readonly string ErrorLog = Path.Combine(Root, "errors.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
    }
}
