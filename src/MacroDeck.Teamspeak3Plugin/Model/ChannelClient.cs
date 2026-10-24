using MacroDeck.Teamspeak3Plugin.Helper;
using System.Globalization;

namespace MacroDeck.Teamspeak3Plugin.Model
{
    // The single status icon TeamSpeak shows in front of a client, in TeamSpeak's own precedence.
    internal enum ChannelClientStatus
    {
        Away,
        OutputHardwareDisabled,
        OutputMuted,
        InputHardwareDisabled,
        InputMuted,
        LocallyMuted,
        Talking,
        Silent,
    }

    internal sealed record ChannelClient
    {
        public required int ClientId { get; init; }

        public required string Nickname { get; init; }

        public int TalkPower { get; init; }

        public bool IsTalker { get; init; }

        public bool IsTalking { get; init; }

        public bool InputMuted { get; init; }

        public bool OutputMuted { get; init; }

        public bool HasInputHardware { get; init; } = true;

        public bool HasOutputHardware { get; init; } = true;

        public bool IsAway { get; init; }

        public bool IsLocallyMuted { get; init; }

        public bool IsChannelCommander { get; init; }

        public ChannelClientStatus Status =>
            IsAway ? ChannelClientStatus.Away
            : !HasOutputHardware ? ChannelClientStatus.OutputHardwareDisabled
            : OutputMuted ? ChannelClientStatus.OutputMuted
            : !HasInputHardware ? ChannelClientStatus.InputHardwareDisabled
            : InputMuted ? ChannelClientStatus.InputMuted
            : IsLocallyMuted ? ChannelClientStatus.LocallyMuted
            : IsTalking ? ChannelClientStatus.Talking
            : ChannelClientStatus.Silent;

        // TeamSpeak lists clients by talk power, then talkers first, then by nickname.
        public static IComparer<ChannelClient> TeamspeakOrder { get; } = Comparer<ChannelClient>.Create((a, b) =>
        {
            var byTalkPower = b.TalkPower.CompareTo(a.TalkPower);
            if (byTalkPower != 0)
            {
                return byTalkPower;
            }

            var byTalker = b.IsTalker.CompareTo(a.IsTalker);
            if (byTalker != 0)
            {
                return byTalker;
            }

            var byName = string.Compare(a.Nickname, b.Nickname, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : a.ClientId.CompareTo(b.ClientId);
        });

        // Applies the ClientQuery properties present in one entry; absent properties keep their value.
        public ChannelClient With(IReadOnlyDictionary<string, string> properties)
        {
            var updated = this;
            foreach (var (key, value) in properties)
            {
                updated = key switch
                {
                    "client_nickname" => updated with { Nickname = Teamspeak3Escaping.Unescape(value) },
                    "client_talk_power" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var power) ? updated with { TalkPower = power } : updated,
                    "client_is_talker" => updated with { IsTalker = value == "1" },
                    "client_flag_talking" => updated with { IsTalking = value == "1" },
                    "client_input_muted" => updated with { InputMuted = value == "1" },
                    "client_output_muted" => updated with { OutputMuted = value == "1" },
                    "client_input_hardware" => updated with { HasInputHardware = value == "1" },
                    "client_output_hardware" => updated with { HasOutputHardware = value == "1" },
                    "client_away" => updated with { IsAway = value == "1" },
                    "client_is_muted" => updated with { IsLocallyMuted = value == "1" },
                    "client_is_channel_commander" => updated with { IsChannelCommander = value == "1" },
                    _ => updated,
                };
            }

            return updated;
        }
    }
}
