using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    // Shared by the input and output mute actions. State ids are persisted once a button adopts them.
    internal static class MuteActionStates
    {
        public const string Unmuted = "unmuted";
        public const string Muted = "muted";
        public const string Unavailable = "unavailable";

        // Answering is a cache read, so a short interval costs nothing.
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private static readonly IReadOnlyList<ActionStateDefinition> States =
        [
            new(Unmuted, MacroDeckStrings.States.Unmuted())
            {
                DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" },
            },
            new(Muted, MacroDeckStrings.States.Muted())
            {
                DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#c53030" },
            },
            new(Unavailable, MacroDeckStrings.States.Unavailable())
            {
                DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" },
            },
        ];

        public static ActionParameter ModeParameter { get; } = ActionParameter.Choice(
            "mode",
            [
                new ActionParameterOption { Label = Strings.Actions.MuteMode.Toggle(), Value = "toggle" },
                new ActionParameterOption { Label = Strings.Actions.MuteMode.On(), Value = "on" },
                new ActionParameterOption { Label = Strings.Actions.MuteMode.Off(), Value = "off" },
            ],
            label: Strings.Actions.MuteMode.Label());

        public static Task<ActionStateSnapshot?> SnapshotFor(bool? muted)
            => Task.FromResult<ActionStateSnapshot?>(new ActionStateSnapshot(States, muted switch
            {
                true => Muted,
                false => Unmuted,
                null => Unavailable,
            }));

        public static string StateIdFor(bool muted) => muted ? Muted : Unmuted;
    }
}
