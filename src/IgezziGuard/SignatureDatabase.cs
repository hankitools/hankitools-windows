namespace IgezziGuard;

public sealed class SignatureDatabase
{
    private readonly Dictionary<string, SignatureEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    public static SignatureDatabase Load()
    {
        var database = new SignatureDatabase();

        // Keep the harmless EICAR test signature available even if the data file
        // was not copied next to a development build.
        database.Add(new SignatureEntry(
            "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f",
            "EICAR-Test-File",
            DetectionSeverity.Malware));

        var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "signatures.txt");
        if (!File.Exists(filePath))
        {
            return database;
        }

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length != 64 ||
                !parts[0].All(Uri.IsHexDigit))
            {
                continue;
            }

            var severity = parts.Length >= 3 &&
                Enum.TryParse<DetectionSeverity>(parts[2], true, out var parsed) &&
                Enum.IsDefined(parsed)
                    ? parsed
                    : DetectionSeverity.Malware;

            database.Add(new SignatureEntry(parts[0].ToLowerInvariant(), parts[1], severity));
        }

        return database;
    }

    public bool TryMatch(string sha256, out SignatureEntry? signature) =>
        _entries.TryGetValue(sha256, out signature);

    private void Add(SignatureEntry entry) => _entries[entry.Sha256] = entry;
}
