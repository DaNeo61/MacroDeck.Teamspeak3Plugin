using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class OutputMuteAction : IActionDefinition, IStateProviderActionDefinition
    {
        public string Id => "output-mute";

        private readonly ILogger _logger;

        public OutputMuteAction(ILogger logger) => _logger = logger.ForContext<OutputMuteAction>();

        public LocalizedText Name => Strings.Actions.OutputMute.Name();

        public LocalizedText Description => Strings.Actions.OutputMute.Description();

        public IReadOnlyList<ActionParameter> Parameters => [MuteActionStates.ModeParameter];

        public TimeSpan StatePollInterval => MuteActionStates.PollInterval;

        public IActionExecutor CreateExecutor() => new Executor(_logger);

        public Task<ActionStateSnapshot?> GetActionStateAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
            => MuteActionStates.SnapshotFor(Teamspeak3Telnet.Instance.OutputMuted);

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

                    var currentMuted = Telnet.GetOutputMuteStatus(cId);
                    var mode = (context.Parameters.GetValueOrDefault("mode") as string)?.Trim().ToLowerInvariant();

                    var targetMuted = mode switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => !currentMuted,
                    };

                    var changed = Telnet.SetOutputMuteStatus(targetMuted ? 1 : 0);
                    if (changed)
                    {
                        return Task.FromResult(ActionResult.Success(MuteActionStates.StateIdFor(targetMuted)));
                    }

                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ToggleFailed()));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to change the output mute state");
                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, ex.Message));
                }
            }
        }
    }
}
