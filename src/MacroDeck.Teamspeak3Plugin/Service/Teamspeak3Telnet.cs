using MacroDeck.Localization;
using MacroDeck.Teamspeak3Plugin.Helper;
using MacroDeck.Teamspeak3Plugin.Model;
using Serilog;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace MacroDeck.Teamspeak3Plugin.Service
{
    internal sealed class Teamspeak3Telnet : IDisposable
    {
        private static readonly Teamspeak3Telnet _instance = new();
        private readonly object _lock = new();

        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private ILogger? _logger;
        private string? _apiKey;

        private const int CommandTimeoutMs = 2000;
        private const int RetryCount = 10;

        public static Teamspeak3Telnet Instance => _instance;

        private volatile int _clientId = -1;

        private const int MuteStateUnknown = -1;
        private volatile int _inputMuted = MuteStateUnknown;
        private volatile int _outputMuted = MuteStateUnknown;

        // Last known mute states, kept current by reads, own changes and notifications so that button
        // state polls never need a ClientQuery round trip.
        public bool? InputMuted => FromStoredMuteState(_inputMuted);

        public bool? OutputMuted => FromStoredMuteState(_outputMuted);

        public int ClientId { get => _clientId; private set => _clientId = value; }

        public bool IsConnected => _client?.Connected == true && _reader is not null && _writer is not null;

        public bool HasApiKey => _apiKey is not null;

        public string? ApiKey
        {
            get
            {
                lock (_lock)
                {
                    return _apiKey;
                }
            }
        }

        // Both are raised outside the lock so a handler may call back into this class.
        public event EventHandler? ApiKeyChanged;

        public event EventHandler? Connected;

        private Teamspeak3Telnet()
        {
        }

        public Teamspeak3Telnet WithLogger(ILogger logger)
        {
            _logger = logger.ForContext<Teamspeak3Telnet>();
            return this;
        }

        public bool ConnectClient(string apiKey, bool forceConnect = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger?.Warning(L(Strings.Service.Teamspeak3Telnet.ConnectClient.ApiKeyEmpty()));
                throw new TimeoutException(L(Strings.Service.Teamspeak3Telnet.ConnectClient.ApiKeyEmpty()));
            }

            string? previousKey;
            lock (_lock)
            {
                if (IsConnected && !forceConnect && apiKey == _apiKey)
                {
                    _logger?.Debug(L(Strings.Service.Teamspeak3Telnet.ConnectClient.AlreadyConnected()));
                    return true;
                }

                previousKey = _apiKey;
                try
                {
                    ConnectInternal(apiKey);
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, L(Strings.Service.Teamspeak3Telnet.ConnectClient.ConnectFailed()));
                    throw;
                }
            }

            if (previousKey != apiKey)
            {
                ApiKeyChanged?.Invoke(this, EventArgs.Empty);
            }

            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void UseApiKey(string? apiKey)
        {
            lock (_lock)
            {
                var normalized = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
                if (normalized == _apiKey)
                {
                    return;
                }

                _apiKey = normalized;
                DisconnectInternal();
            }

            ApiKeyChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool EnsureConnected() => TryReconnect(out _);

        public bool TryReconnect(out string? error)
        {
            lock (_lock)
            {
                error = null;
                if (IsConnected)
                {
                    return true;
                }

                if (_apiKey is null)
                {
                    error = "No API key configured.";
                    return false;
                }

                try
                {
                    ConnectInternal(_apiKey);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool CheckConnection()
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    return false;
                }

                var response = GetTelnetResponse("whoami");
                if (!IsConnected)
                {
                    return false;
                }

                // The ClientQuery socket survives the TeamSpeak client reconnecting to a server, but the
                // client id changes with it, so the cached id is refreshed on every check.
                var previousClientId = ClientId;
                ClientId = response.Contains("msg=ok", StringComparison.Ordinal) && TryParseKeyIntValue(response, "clid=", out var clid) ? clid : -1;
                if (ClientId == -1)
                {
                    ResetMuteStates();
                }
                else if (ClientId != previousClientId || InputMuted is null || OutputMuted is null)
                {
                    RefreshMuteStates();
                }

                return true;
            }
        }

        public void ApplyMuteNotification(bool? inputMuted, bool? outputMuted)
        {
            if (inputMuted.HasValue)
            {
                _inputMuted = ToStoredMuteState(inputMuted.Value);
            }

            if (outputMuted.HasValue)
            {
                _outputMuted = ToStoredMuteState(outputMuted.Value);
            }
        }

        private void RefreshMuteStates()
        {
            GetInputMuteStatus(ClientId);
            GetOutputMuteStatus(ClientId);
        }

        private void ResetMuteStates()
        {
            _inputMuted = MuteStateUnknown;
            _outputMuted = MuteStateUnknown;
        }

        private static int ToStoredMuteState(bool muted) => muted ? 1 : 0;

        private static bool? FromStoredMuteState(int state) => state == MuteStateUnknown ? null : state == 1;

        private void ConnectInternal(string apiKey)
        {
            try
            {
                DisconnectInternal();

                _client = new TcpClient();
                var connectTask = _client.ConnectAsync("127.0.0.1", 25639);
                if (!connectTask.Wait(CommandTimeoutMs))
                {
                    throw new TimeoutException(L(Strings.Service.Teamspeak3Telnet.ConnectClient.ConnectionTimeout()));
                }

                var stream = _client.GetStream();
                stream.ReadTimeout = CommandTimeoutMs;
                stream.WriteTimeout = CommandTimeoutMs;
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };

                var welcome = ReadWelcomeWithTimeout();
                if (string.IsNullOrWhiteSpace(welcome) || !welcome.Contains("TS3 Client", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(LF(Strings.Service.Teamspeak3Telnet.ConnectClient.UnexpectedWelcome(), welcome));
                }

                var authResponse = GetTelnetResponse($"auth apikey={Escape(apiKey)}");
                if (!authResponse.Contains("msg=ok", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(LF(Strings.Service.Teamspeak3Telnet.ConnectClient.AuthenticationFailed(), authResponse));
                }

                _apiKey = apiKey;
                ClientId = GetClientId();
                if (ClientId != -1)
                {
                    RefreshMuteStates();
                }

                _logger?.Information(LF(Strings.Service.Teamspeak3Telnet.ConnectClient.Connected(), ClientId));
            }
            catch
            {
                DisconnectInternal();
                throw;
            }
        }

        public int GetClientId()
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    _logger?.Warning(L(Strings.Service.Teamspeak3Telnet.GetClientId.NoConnection()));
                    return -1;
                }

                if (ClientId != -1)
                {
                    return ClientId;
                }

                for (var retry = 0; retry < RetryCount; retry++)
                {
                    var response = GetTelnetResponse("whoami");
                    if (response.Contains("msg=ok", StringComparison.Ordinal) && TryParseKeyIntValue(response, "clid=", out var clid))
                    {
                        ClientId = clid;
                        return clid;
                    }
                }

                _logger?.Warning(L(Strings.Service.Teamspeak3Telnet.GetClientId.NotFound()));
                return -1;
            }
        }


        #region Channel Stuff
        public bool ChannelSwitch(string channelName, int clientId, string password)
        {
            lock (_lock)
            {
                if (!IsConnected) return false;
                if (string.IsNullOrWhiteSpace(channelName)) return false;

                var command = new StringBuilder("clientmove");

                channelName = channelName.FixTs3SpecificChars();

                try
                {
                    var channelList = GetChannelListInternal();
                    if (channelList == null || channelList.Count == 0)
                        return false;

                    var channel = channelList.FirstOrDefault(o => o.Name == channelName);
                    if (channel == null)
                        return false;

                    command.Append($" cid={channel.ChannelId}");
                    command.Append($" clid={clientId}");

                    if(!string.IsNullOrEmpty(password))
                        command.Append($" cpw={password}");

                    var channelSwitchResponse = GetTelnetResponse(command.ToString());
                    return channelSwitchResponse.Contains("msg=ok");
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, $"Error switching channel: {ex.Message}");
                    return false;
                }
            }
        }
        public bool ChannelSwitch(int channelId, int clientId, string password)
        {
            lock (_lock)
            {
                if (!IsConnected) return false;

                var command = new StringBuilder("clientmove");

                try
                {
                    command.Append($" cid={channelId}");
                    command.Append($" clid={clientId}");

                    if (!string.IsNullOrEmpty(password))
                        command.Append($" cpw={password}");

                    var channelSwitchResponse = GetTelnetResponse(command.ToString());
                    return channelSwitchResponse.Contains("msg=ok");
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, $"Error switching channel: {ex.Message}");
                    return false;
                }
            }
        }

        public List<TeamspeakChannel> GetChannelList()
        {
            lock (_lock)
            {
                return GetChannelListInternal();
            }
        }

        private List<TeamspeakChannel> GetChannelListInternal()
        {
            var channels = new List<TeamspeakChannel>();

            if (!IsConnected)
                return channels;

            try
            {
                var channelListResponse = GetTelnetResponse("channellist");
                if (string.IsNullOrEmpty(channelListResponse) || !channelListResponse.Contains("msg=ok"))
                    return channels;

                foreach (var tempChannel in channelListResponse.Split("|"))
                {
                    if (string.IsNullOrWhiteSpace(tempChannel))
                        continue;

                    var channel = new TeamspeakChannel(tempChannel);
                    if (channel != null)
                        channels.Add(channel);
                }

                return channels;
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, $"Error getting channel list: {ex.Message}");
                return channels;
            }
        }

        public string GetCurrentChannel(int clientId)
        {
            lock (_lock)
            {
                if (!IsConnected) return string.Empty;
                for (int retries = 0; retries < RetryCount; retries++)
                {
                    try
                    {
                        var whoAmIResponse = GetTelnetResponse($"whoami");
                        if (whoAmIResponse.Contains("msg=ok") && TryParseKeyIntValue(whoAmIResponse, "cid=", out var channelId))
                        {
                            var channelList = GetChannelListInternal();
                            var channel = channelList.FirstOrDefault(c => c.ChannelId == channelId.ToString());
                            return channel?.Name ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(ex, $"Error getting current channel: {ex.Message}");
                    }
                }
                return string.Empty;
            }
        }
        public string CurrentChannelVariable()
        {
            return (ClientId != -1 ? GetCurrentChannel(ClientId) : "Not Connected");
        }
        #endregion


        #region Input Stuff
        public bool GetInputMuteStatus(int clientId)
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    _logger?.Warning(L(Strings.Service.Teamspeak3Telnet.GetInputMuteStatus.NoConnection()));
                    return false;
                }

                for (var retry = 0; retry < RetryCount; retry++)
                {
                    var response = GetTelnetResponse($"clientvariable clid={clientId} client_input_muted");
                    if (response.Contains("msg=ok", StringComparison.Ordinal) && TryParseKeyIntValue(response, "client_input_muted=", out var state))
                    {
                        _inputMuted = ToStoredMuteState(state == 1);
                        return state == 1;
                    }
                }

                _logger?.Warning(LF(Strings.Service.Teamspeak3Telnet.GetInputMuteStatus.InvalidResponse(), clientId));
                return false;
            }
        }

        public bool SetInputMuteStatus(int inputMuteStatus)
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    _logger?.Warning(L(Strings.Service.Teamspeak3Telnet.SetInputMuteStatus.NoConnection()));
                    return false;
                }

                for (var retry = 0; retry < RetryCount; retry++)
                {
                    var response = GetTelnetResponse($"clientupdate client_input_muted={inputMuteStatus}");
                    if (response.Contains("msg=ok", StringComparison.Ordinal))
                    {
                        _inputMuted = ToStoredMuteState(inputMuteStatus == 1);
                        _logger?.Information(LF(Strings.Service.Teamspeak3Telnet.SetInputMuteStatus.Success(), inputMuteStatus));
                        return true;
                    }
                }

                _logger?.Warning(LF(Strings.Service.Teamspeak3Telnet.SetInputMuteStatus.Failed(), inputMuteStatus));
                return false;
            }
        }

        public bool InputMuteVariable(bool? state = null)
        {
            
            return state ??= (ClientId != -1 && GetInputMuteStatus(ClientId));
        }
        #endregion

        #region Output Stuff
        public bool GetOutputMuteStatus(int clientId)
        {
            lock (_lock)
            {
                if (!IsConnected)
                    return false;

                for (int retries = 0; retries < RetryCount; retries++)
                {
                    try
                    {
                        var outputMuteStatusResponse = GetTelnetResponse($"clientvariable clid={clientId} client_output_muted");
                        if (outputMuteStatusResponse.Contains("msg=ok") && TryParseKeyIntValue(outputMuteStatusResponse, "client_output_muted=", out var state))
                        {
                            _outputMuted = ToStoredMuteState(state == 1);
                            return state == 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(ex, $"Error getting output mute status: {ex.Message}");
                    }
                }

                return false;
            }
        }

        public bool SetOutputMuteStatus(int outputMuteStatus)
        {
            lock (_lock)
            {
                if (!IsConnected) return false;

                for (int retries = 0; retries < RetryCount; retries++)
                {
                    try
                    {
                        var setOutputMuteStatusResponse = GetTelnetResponse($"clientupdate client_output_muted={outputMuteStatus}");
                        if (setOutputMuteStatusResponse.Contains("msg=ok"))
                        {
                            _outputMuted = ToStoredMuteState(outputMuteStatus == 1);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(ex, $"Error setting output mute status: {ex.Message}");
                    }
                }

                return false;
            }
        }
        public bool OutputMuteVariable()
        {
            return (ClientId != -1 && GetOutputMuteStatus(ClientId));
        }
        #endregion


        #region Nickname Stuff
        public bool ChangeNickname(string nickname)
        {
            lock (_lock)
            {
                if (!IsConnected)
                    return false;

                if (string.IsNullOrWhiteSpace(nickname))
                    return false;

                nickname = nickname.FixTs3SpecificChars();

                try
                {
                    var changeNicknameResponse = GetTelnetResponse($"clientupdate client_nickname={nickname}");
                    return changeNicknameResponse.Contains("msg=ok");
                }
                catch (Exception ex)
                {
                    _logger?.Warning(ex, $"Error changing nickname: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion

        public void Dispose()
        {
            bool hadApiKey;
            lock (_lock)
            {
                hadApiKey = _apiKey is not null;
                _apiKey = null;
                DisconnectInternal();
            }

            if (hadApiKey)
            {
                ApiKeyChanged?.Invoke(this, EventArgs.Empty);
            }

            GC.SuppressFinalize(this);
        }

        private void DisconnectInternal()
        {
            try
            {
                _writer?.Dispose();
                _reader?.Dispose();
                _client?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, L(Strings.Service.Teamspeak3Telnet.Disconnect.Failed()));
            }
            finally
            {
                _writer = null;
                _reader = null;
                _client = null;
                ClientId = -1;
                ResetMuteStates();
            }
        }

        private string GetTelnetResponse(string command)
        {
            if (!IsConnected)
            {
                return string.Empty;
            }

            try
            {
                WriteLineWithTimeout(command);
                return ReadWithTimeout();
            }
            catch (Exception ex)
            {
                // Only the command name: arguments can carry the API key or a channel password.
                _logger?.Warning(ex, LF(Strings.Service.Teamspeak3Telnet.TelnetCommand.Failed(), command.Split(' ')[0]));
                DisconnectInternal();
                return string.Empty;
            }
        }

        private void WriteLineWithTimeout(string line)
        {
            if (_writer is null)
            {
                throw new InvalidOperationException(L(Strings.Service.Teamspeak3Telnet.Writer.NotInitialized()));
            }

            try
            {
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (IOException)
            {
                throw new TimeoutException(LF(Strings.Service.Teamspeak3Telnet.Writer.WriteTimeout(), line));
            }
            catch (ObjectDisposedException)
            {
                throw new TimeoutException(L(Strings.Service.Teamspeak3Telnet.Writer.FlushTimeout()));
            }
        }

        private string ReadWithTimeout()
        {
            if (_reader is null)
            {
                throw new InvalidOperationException(L(Strings.Service.Teamspeak3Telnet.Reader.NotInitialized()));
            }

            var lines = new List<string>();
            while (true)
            {
                string? line;
                try
                {
                    line = _reader.ReadLine();
                }
                catch (IOException)
                {
                    throw new TimeoutException(L(Strings.Service.Teamspeak3Telnet.Reader.ReadTimeout()));
                }

                if (line is null)
                {
                    throw new IOException("The TeamSpeak client closed the ClientQuery connection.");
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }

                if (line.StartsWith("error ", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return string.Join("\n", lines);
        }

        private string ReadWelcomeWithTimeout()
        {
            if (_reader is null)
            {
                throw new InvalidOperationException(L(Strings.Service.Teamspeak3Telnet.Reader.NotInitialized()));
            }

            var lines = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    var line = _reader.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line);
                        if (line.Contains("TS3 Client", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    break;
                }
            }

            return string.Join("\n", lines);
        }

        internal static string Escape(string value)
            => value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("/", "\\/", StringComparison.Ordinal)
                .Replace("|", "\\p", StringComparison.Ordinal)
                .Replace(" ", "\\s", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);

        private bool TryParseKeyIntValue(string response, string key, out int value)
        {
            value = -1;
            try
            {
                var parts = response.Split(new[] { key }, StringSplitOptions.None);
                if (parts.Length <= 1)
                {
                    return false;
                }

                var token = parts[1].Split('\n', ' ')[0].Trim();
                return int.TryParse(token, out value);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, L(Strings.Service.Teamspeak3Telnet.ParseInteger.Failed()));
                return false;
            }
        }

        private static string L(LocalizedText text) => text.ToString();

        private static string LF(LocalizedText text, params object?[] args)
            => string.Format(CultureInfo.CurrentCulture, text.ToString(), args);
    }
}
