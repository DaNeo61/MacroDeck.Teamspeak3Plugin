namespace MacroDeck.Teamspeak3Plugin.Helper
{
    public static class StringClassExtension
    {
        public static string FixTs3SpecificChars(this string @this)
        {
            var fixedString = @this
                .Replace(" ", "\\s")
                .Replace("|", "\\p");

            return fixedString;
        }
        public static string Substring(this string @this, string from = null, string until = null, StringComparison comparison = StringComparison.InvariantCulture)
        {
            var fromLength = (from ?? string.Empty).Length;
            var startIndex = !string.IsNullOrEmpty(from)
                ? @this.IndexOf(from, comparison) + fromLength
                : 0;

            if (startIndex < fromLength)
                throw new ArgumentException("from: Failed to find an instance of the first anchor");

            var endIndex = !string.IsNullOrEmpty(until)
                ? @this.IndexOf(until, startIndex, comparison)
                : @this.Length;

            if (endIndex < 0) throw new ArgumentException("until: Failed to find an instance of the last anchor");

            var subString = @this.Substring(startIndex, endIndex - startIndex);
            return subString;
        }

    }
}
