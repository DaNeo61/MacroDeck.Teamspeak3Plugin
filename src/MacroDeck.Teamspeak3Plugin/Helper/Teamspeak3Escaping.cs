using System.Text;

namespace MacroDeck.Teamspeak3Plugin.Helper
{
    // ClientQuery escapes whitespace, pipes and slashes in every value it sends or receives.
    internal static class Teamspeak3Escaping
    {
        public static string Escape(string value)
            => value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("/", "\\/", StringComparison.Ordinal)
                .Replace("|", "\\p", StringComparison.Ordinal)
                .Replace(" ", "\\s", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);

        public static string Unescape(string value)
        {
            if (!value.Contains('\\', StringComparison.Ordinal))
            {
                return value;
            }

            var result = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i == value.Length - 1)
                {
                    result.Append(value[i]);
                    continue;
                }

                i++;
                result.Append(value[i] switch
                {
                    's' => ' ',
                    'p' => '|',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'v' => '\v',
                    var other => other,
                });
            }

            return result.ToString();
        }
    }
}
