namespace AtspmDocsGenerator;

internal static class OutputLayout
{
    public const string SupportDirectory = "support";
    public const string LogFileName = "support/log-messages.md";
    public const string TemplatePageFileName = "support/configuration-templates.md";

    public static string SupportFile(string name) => $"support/{name}";

    public static string ExistingPath(string root, string name)
    {
        var path = Path.Combine(root, name);
        return File.Exists(path) ? path : Path.Combine(root, Path.GetFileName(name));
    }

    public static void CreateSupportDirectory(string root) =>
        Directory.CreateDirectory(Path.Combine(root, SupportDirectory));
}
