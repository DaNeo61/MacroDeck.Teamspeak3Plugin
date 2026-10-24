using MacroDeck.Teamspeak3Plugin.Helper;
using MacroDeck.Teamspeak3Plugin.Model;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    internal sealed record Teamspeak3Notification(
        string Name,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Entries,
        IReadOnlyCollection<string> AffectedVariableIds,
        bool ServerConnectionChanged,
        bool? InputMuted = null,
        bool? OutputMuted = null)
    {
        public static readonly IReadOnlyList<string> RegisteredEvents =
        [
            "notifyclientupdated",
            "notifyclientmoved",
            "notifycliententerview",
            "notifyclientleftview",
            "notifytalkstatuschange",
            "notifychanneledited",
            "notifychanneldeleted",
            "notifyconnectstatuschange",
            "notifycurrentserverconnectionchanged",
        ];

        public static Teamspeak3Notification Parse(string line, int ownClientId)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("notify", StringComparison.Ordinal))
            {
                return new Teamspeak3Notification(string.Empty, [], [], false);
            }

            var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
            var name = separator < 0 ? trimmed : trimmed[..separator];
            IReadOnlyList<IReadOnlyDictionary<string, string>> entries = separator < 0
                ? []
                : trimmed[(separator + 1)..].Split('|').Select(Teamspeak3Response.ParseEntry).ToList();
            var none = new Teamspeak3Notification(name, entries, [], false);

            switch (name)
            {
                case "notifyconnectstatuschange":
                case "notifycurrentserverconnectionchanged":
                    return none with { AffectedVariableIds = VariableIds.All, ServerConnectionChanged = true };

                case "notifychanneledited":
                case "notifychanneldeleted":
                    return none with { AffectedVariableIds = [VariableIds.CurrentChannel] };

                case "notifyclientmoved":
                    return entries.Any(e => IsOwnClient(e, ownClientId))
                        ? none with { AffectedVariableIds = [VariableIds.CurrentChannel] }
                        : none;

                case "notifyclientupdated":
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    bool? inputMuted = null;
                    bool? outputMuted = null;
                    foreach (var entry in entries.Where(e => IsOwnClient(e, ownClientId)))
                    {
                        if (entry.TryGetValue("client_input_muted", out var input))
                        {
                            ids.Add(VariableIds.InputState);
                            inputMuted = input == "1";
                        }

                        if (entry.TryGetValue("client_output_muted", out var output))
                        {
                            ids.Add(VariableIds.OutputState);
                            outputMuted = output == "1";
                        }
                    }

                    return ids.Count == 0
                        ? none
                        : none with { AffectedVariableIds = ids, InputMuted = inputMuted, OutputMuted = outputMuted };

                default:
                    return none;
            }
        }

        // An unknown own id (not connected to a server yet) accepts every client, since a spare refresh is cheap.
        private static bool IsOwnClient(IReadOnlyDictionary<string, string> entry, int ownClientId)
            => ownClientId == -1
                || !entry.TryGetValue("clid", out var clid)
                || clid == ownClientId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
