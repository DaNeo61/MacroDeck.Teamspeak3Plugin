using MacroDeck.Teamspeak3Plugin.Model;

namespace MacroDeck.Teamspeak3Plugin.Widgets
{
    // One line of the widget: a client, or the closing "+N more" line when not everyone fits.
    internal sealed record ChannelWidgetRow(string Key, ChannelClient? Client, int HiddenCount);

    internal static class ChannelWidgetRows
    {
        // Everyone fits: TeamSpeak's own order. Too many: talking clients come first so a speaker is
        // never hidden, then the rest in TeamSpeak's order, and the last line counts who is left out.
        public static IReadOnlyList<ChannelWidgetRow> Select(IReadOnlyList<ChannelClient> clients, int capacity)
        {
            var ordered = clients.Order(ChannelClient.TeamspeakOrder).ToList();
            if (ordered.Count <= capacity)
            {
                return ordered.Select(ToRow).ToList();
            }

            var shown = Math.Max(capacity - 1, 0);
            var rows = ordered
                .Where(client => client.IsTalking)
                .Concat(ordered.Where(client => !client.IsTalking))
                .Take(shown)
                .Select(ToRow)
                .ToList();
            rows.Add(new ChannelWidgetRow("more", null, ordered.Count - shown));
            return rows;
        }

        public static IReadOnlyList<ChannelWidgetRow> Column(IReadOnlyList<ChannelWidgetRow> rows, int column, int rowsPerColumn)
            => rows.Skip(column * rowsPerColumn).Take(rowsPerColumn).ToList();

        private static ChannelWidgetRow ToRow(ChannelClient client)
            => new($"c{client.ClientId}", client, 0);
    }
}
