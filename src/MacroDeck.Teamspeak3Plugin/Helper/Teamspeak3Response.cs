namespace MacroDeck.Teamspeak3Plugin.Helper
{
    // Parses ClientQuery data lines: entries separated by '|', each a space separated list of key=value.
    internal static class Teamspeak3Response
    {
        public static bool IsOk(string response)
            => response.Contains("error id=0 msg=ok", StringComparison.Ordinal);

        public static IReadOnlyList<IReadOnlyDictionary<string, string>> Entries(string response)
        {
            var data = string.Join(' ', response
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0
                    && !line.StartsWith("error ", StringComparison.Ordinal)
                    && !line.StartsWith("notify", StringComparison.Ordinal)));

            return data.Length == 0 ? [] : data.Split('|').Select(ParseEntry).ToList();
        }

        public static IReadOnlyDictionary<string, string> ParseEntry(string entry)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var token in entry.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = token.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0)
                {
                    values[token] = string.Empty;
                }
                else
                {
                    values[token[..equals]] = token[(equals + 1)..];
                }
            }

            return values;
        }

        public static int? IntValue(IReadOnlyDictionary<string, string> entry, string key)
            => entry.TryGetValue(key, out var value) && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
    }
}
