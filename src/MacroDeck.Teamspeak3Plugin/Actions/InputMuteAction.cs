using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class InputMuteAction : IActionDefinition
    {
        public string Id => "input-mute";

        private readonly ILogger _logger;

        public InputMuteAction(ILogger logger) => _logger = logger.ForContext<InputMuteAction>();

        public LocalizedText Name => Strings.Actions.InputMute.Name();

        public LocalizedText Description => Strings.Actions.InputMute.Description();

        public IReadOnlyList<ActionParameter> Parameters =>
        [
            ActionParameter.Choice(
                "mode",
                [
                    new ActionParameterOption { Label = Strings.Actions.MuteMode.Toggle().ToString(), Value = "toggle" },
                    new ActionParameterOption { Label = Strings.Actions.MuteMode.On().ToString(), Value = "on" },
                    new ActionParameterOption { Label = Strings.Actions.MuteMode.Off().ToString(), Value = "off" },
                ]),
        ];

        public IActionExecutor CreateExecutor() => new Executor(_logger);

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
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.NotConnected().ToString()));
                    }

                    var cId = Telnet.GetClientId();
                    if (cId == -1)
                    {
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.ClientIdNotFound().ToString()));
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
                        return ActionResult.SucceededTask;
                    }

                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ToggleFailed().ToString()));
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, Strings.Actions.Errors.ToggleException().ToString());
                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, ex.Message));
                }
            }
        }
    }
}
