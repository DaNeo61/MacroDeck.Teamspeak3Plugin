using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class SwitchChannelAction : IDynamicOptionsActionDefinition
    {
        public string Id => "switch-channel";

        private readonly ILogger _logger;

        public SwitchChannelAction(ILogger logger) => _logger = logger.ForContext<SwitchChannelAction>();

        public LocalizedText Name => Strings.Actions.SwitchChannel.Name();

        public LocalizedText Description => Strings.Actions.SwitchChannel.Description();

        public IReadOnlyList<ActionParameter> Parameters => 
        [
            ActionParameter.DynamicChoice("channel-list", Strings.Actions.SwitchChannel.Channel.Label()),
            ActionParameter.Secret("channel-password", Strings.Actions.SwitchChannel.Password.Label(), Strings.Actions.SwitchChannel.Password.Description())
        ];

        public IActionExecutor CreateExecutor() => new Executor(_logger);

        public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context, CancellationToken cancellationToken)
        {
            var list = new List<ActionParameterOption>();
            if (context.ParameterName == "channel-list")
            {
                var channels = Teamspeak3Telnet.Instance.GetChannelList();
                if (!channels.Any())
                    return new DynamicOptionsResult { Options = [], Error = Strings.Errors.NotConnected() };

                foreach ( var channel in channels)
                {
                    list.Add(new ActionParameterOption { Label = channel.Name, Value = channel.ChannelId});
                }
            }
            return new DynamicOptionsResult { Options = list, CacheSeconds = 15 };
        }

        private sealed class Executor : IActionExecutor
        {
            private readonly ILogger _logger;

            public Executor(ILogger logger) => _logger = logger;

            private Teamspeak3Telnet Telnet => Teamspeak3Telnet.Instance;

            public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
            {
                try
                {
                    if (!Telnet.EnsureConnected())
                    {
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.NotConnected()));
                    }

                    var cId = Telnet.GetClientId();
                    if (cId == -1)
                    {
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.ClientIdNotFound()));
                    }
                    var changed = false;
                    var channelId = context.Parameters.GetValueOrDefault("channel-list") as string;
                    var channelPw = context.Parameters.GetValueOrDefault("channel-password") as string;
                    if(!string.IsNullOrEmpty(channelId) && int.TryParse(channelId, out var parsedId))
                        changed = Telnet.ChannelSwitch(parsedId, cId, channelPw);

                    return changed ? ActionResult.SucceededTask : Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ToggleFailed()));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to switch the channel");
                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, ex.Message));
                }
            }
        }
    }
}
