using System.Text.Json.Nodes;

namespace AtspmDocsGenerator;

internal static class ConfigurationExampleTree
{
    public static JsonObject Section(JsonObject root, string sectionName)
    {
        // Named bindings (Database:Main) become nested JSON objects.
        var current = root;
        foreach (var segment in sectionName.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current[segment] is not JsonObject child)
                current[segment] = child = new JsonObject();
            current = child;
        }
        return current;
    }
}
