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

        public IReadOnlyList<ActionParameter> Parameters => [];

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
                    if (!Telnet.IsConnected)
                    {
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.NotConnected().ToString()));
                    }

                    var cId = Telnet.GetClientId();
                    if (cId == -1)
                    {
                        return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Errors.ClientIdNotFound().ToString()));
                    }

                    var inputMuteStatus = Telnet.GetInputMuteStatus(cId);
                    var changed = Telnet.SetInputMuteStatus(inputMuteStatus ? 0 : 1);

                    VariableValue.Of("ts3-input-state", changed);
                    return changed
                        ? ActionResult.SucceededTask
                        : Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, Strings.Actions.Errors.ToggleFailed().ToString()));
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
