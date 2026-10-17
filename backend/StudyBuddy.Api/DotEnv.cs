namespace StudyBuddy.Api;

/// <summary>
/// Loads KEY=VALUE pairs from the nearest .env file (searching up from the working directory)
/// into environment variables, so `dotnet run` uses the same config file as docker compose.
/// Variables that are already set win, so real environment variables (e.g. on a hosting
/// provider, where no .env file exists) always take precedence.
/// </summary>
public static class DotEnv
{
    public static void Load()
    {
        var path = Find(Directory.GetCurrentDirectory());
        if (path is null) return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            {
                value = value[1..^1];
            }

            if (value.Length > 0 && Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? Find(string? directory)
    {
        for (; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var candidate = Path.Combine(directory, ".env");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
