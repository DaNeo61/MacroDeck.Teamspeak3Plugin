using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class InputMuteAction : IActionDefinition, IStateProviderActionDefinition
    {
        public string Id => "input-mute";

        private readonly ILogger _logger;

        public InputMuteAction(ILogger logger) => _logger = logger.ForContext<InputMuteAction>();

        public LocalizedText Name => Strings.Actions.InputMute.Name();

        public LocalizedText Description => Strings.Actions.InputMute.Description();

        public IReadOnlyList<ActionParameter> Parameters => [MuteActionStates.ModeParameter];

        public TimeSpan StatePollInterval => MuteActionStates.PollInterval;

        public IActionExecutor CreateExecutor() => new Executor(_logger);

        public Task<ActionStateSnapshot?> GetActionStateAsync(IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
            => MuteActionStates.SnapshotFor(Teamspeak3Telnet.Instance.InputMuted);

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

                    var currentMuted = Telnet.GetInputMuteStatus(cId);
                    var mode = (context.Parameters.GetValueOrDefault("mode") as string)?.Trim().ToLowerInvariant();

                    var targetMuted = mode switch
                    {
                        "on" => true,
                        "off" => false,
                        _ => !currentMuted,
                    };

                    var changed = Telnet.SetInputMuteStatus(targetMuted ? 1 : 0);
                    if (changed)
                    {
                        return Task.FromResult(ActionResult.Success(MuteActionStates.StateIdFor(targetMuted)));
                    }

                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ToggleFailed()));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to change the input mute state");
                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, ex.Message));
                }
            }
        }
    }
}
