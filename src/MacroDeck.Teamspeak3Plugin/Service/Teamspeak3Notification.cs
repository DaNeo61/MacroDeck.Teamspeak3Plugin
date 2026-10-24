using MacroDeck.Teamspeak3Plugin.Model;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    internal sealed record Teamspeak3Notification(IReadOnlyCollection<string> AffectedVariableIds, bool ServerConnectionChanged)
    {
        public static readonly IReadOnlyList<string> RegisteredEvents =
        [
            "notifyclientupdated",
            "notifyclientmoved",
            "notifychanneledited",
            "notifychanneldeleted",
            "notifyconnectstatuschange",
            "notifycurrentserverconnectionchanged",
        ];

        private static readonly Teamspeak3Notification None = new([], false);

        public static Teamspeak3Notification Parse(string line, int ownClientId)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("notify", StringComparison.Ordinal))
            {
                return None;
            }

            var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
            var name = separator < 0 ? trimmed : trimmed[..separator];
            var entries = separator < 0
                ? []
                : trimmed[(separator + 1)..].Split('|').Select(ParseEntry).ToList();

            switch (name)
            {
                case "notifyconnectstatuschange":
                case "notifycurrentserverconnectionchanged":
                    return new Teamspeak3Notification(VariableIds.All, true);

                case "notifychanneledited":
                case "notifychanneldeleted":
                    return new Teamspeak3Notification([VariableIds.CurrentChannel], false);

                case "notifyclientmoved":
                    return entries.Any(e => IsOwnClient(e, ownClientId))
                        ? new Teamspeak3Notification([VariableIds.CurrentChannel], false)
                        : None;

                case "notifyclientupdated":
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var entry in entries.Where(e => IsOwnClient(e, ownClientId)))
                    {
                        if (entry.ContainsKey("client_input_muted"))
                        {
                            ids.Add(VariableIds.InputState);
                        }

                        if (entry.ContainsKey("client_output_muted"))
                        {
                            ids.Add(VariableIds.OutputState);
                        }
                    }

                    return ids.Count == 0 ? None : new Teamspeak3Notification(ids, false);

                default:
                    return None;
            }
        }

        // An unknown own id (not connected to a server yet) accepts every client, since a spare refresh is cheap.
        private static bool IsOwnClient(Dictionary<string, string> entry, int ownClientId)
            => ownClientId == -1
                || !entry.TryGetValue("clid", out var clid)
                || clid == ownClientId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static Dictionary<string, string> ParseEntry(string entry)
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
    }
}
