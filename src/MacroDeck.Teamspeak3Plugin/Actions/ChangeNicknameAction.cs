using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Teamspeak3Plugin.Service;
using Serilog;

namespace MacroDeck.Teamspeak3Plugin.Actions
{
    internal class ChangeNicknameAction : IActionDefinition
    {
        public string Id => "change-nickname";

        private readonly ILogger _logger;

        public ChangeNicknameAction(ILogger logger) => _logger = logger.ForContext<ChangeNicknameAction>();

        public LocalizedText Name => Strings.Actions.ChangeNickname.Name();

        public LocalizedText Description => Strings.Actions.ChangeNickname.Description();

        public IReadOnlyList<ActionParameter> Parameters => [
            ActionParameter.Text("new_nickname", label: Strings.Actions.ChangeNickname.NewNickname(), required: true),
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

                    var newNickname = context.Parameters.GetValueOrDefault("new_nickname") as string;
                    Telnet.ChangeNickname(newNickname);

                    return ActionResult.SucceededTask;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, Strings.Errors.ActionCantBeDone().ToString());
                    return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected, ex.Message));
                }
            }
        }
    }
}
