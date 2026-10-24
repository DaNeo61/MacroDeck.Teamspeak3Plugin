namespace MacroDeck.Teamspeak3Plugin.Model
{
    internal static class VariableIds
    {
        public const string CurrentChannel = "ts3-current-channel";
        public const string OutputState = "ts3-output-state";
        public const string InputState = "ts3-input-state";

        public static readonly IReadOnlyList<string> All = [CurrentChannel, OutputState, InputState];
    }
}
