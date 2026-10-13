using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class OutputMuteAction : IActionDefinition
    {
        public string Id => "output-mute";

        private readonly ILogger _logger;

        public OutputMuteAction(ILogger logger) => _logger = logger.ForContext<OutputMuteAction>();

        public LocalizedText Name => Strings.Actions.OutputMute.Name();

        public LocalizedText Description => Strings.Actions.OutputMute.Description();

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

                    var outputMuteStatus = Telnet.GetOutputMuteStatus(cId);
                    var changed = Telnet.SetOutputMuteStatus(outputMuteStatus ? 0 : 1);

                    VariableValue.Of("ts3-output-state", changed);

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
