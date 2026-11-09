using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Net;
using HomeAssistant.Agent.Plugin.Discord.Contracts;
using HomeAssistant.Agent.Plugin.Discord.Contracts.Responses;
using HomeAssistant.Agent.Plugin.Discord.Enums;
using HomeAssistant.Agent.Plugin.Discord.Exceptions;
using HomeAssistant.Agent.Plugin.Discord.Helpers;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Discord.Discord;

// Talks to the Discord desktop app over its local RPC pipe (discord-ipc-N).
//
// Every message is a frame: opcode and length (both uint32, little endian),
// then the JSON. The client opens with a Handshake frame, Discord answers
// with a READY dispatch, and the client then authorizes (OAuth, a prompt in
// Discord the first time) and authenticates. Commands carry a nonce that
// their response echoes; subscribed events arrive as DISPATCH.
// Waiting for Discord and reconnecting is ReconnectingClient's.
public sealed class DiscordClient : ReconnectingClient
{
    private const int HandshakeVersion = 1;
    private const int TokenRefreshBufferHours = 48;
    private const string StorageTokenKey = "auth-token";
    private const string TokenEndpoint = "https://discord.com/api/oauth2/token";

    // Discord numbers its pipes from 0 and takes the first free one.
    private const int PipeCount = 10;
    private const int PipeConnectTimeoutMs = 1000;

    // Far above anything the RPC sends; a larger length means the stream is
    // out of step and the connection is better dropped.
    private const int MaxFrameSize = 1024 * 1024;

    // Discord's error code for the user rejecting the authorization prompt.
    private const int AuthRejectedCode = 5000;

    // Stable, PTB and Canary all serve the same RPC.
    private static readonly string[] ProcessNames = ["Discord", "DiscordPTB", "DiscordCanary"];

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] ScopesRequired = ["identify", "rpc", "rpc.activities.write"];

    // One instance for the process: a client per request leaks sockets.
    private static readonly HttpClient Http = new();

    private readonly IPluginStorage _storage;
    private readonly string _clientId;
    private readonly string _clientSecret;

    // Commands waiting for their response, by nonce.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Payload?>> _pendingCommands = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NamedPipeClientStream? _pipe;

    // All events fire on the connection loop's background thread.
    public event Action<ChannelSelectResponse>? VoiceChannelSelected;
    public event Action<VoiceSettingsResponse>? VoiceSettingsUpdated;

    public DiscordClient(string clientId, string clientSecret, IPluginStorage storage, ILogger logger)
        : base("Discord", logger)
    {
        _storage = storage;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    protected override TimeSpan ReconnectDelay => TimeSpan.FromSeconds(5);

    protected override TimeSpan AppPollInterval => TimeSpan.FromSeconds(3);

    protected override bool? IsAppRunning() => LocalApp.IsRunningOnWindows(ProcessNames);

    protected override void Dispose(bool disposing)
    {
        _pipe?.Dispose();
        _writeLock.Dispose();
    }

    // waitForResponse: some commands (SUBSCRIBE/UNSUBSCRIBE) are fire-and-forget
    // — Discord acts on the frame the moment it is written and its ack carries
    // nothing we use. Waiting for that ack only produces a false "timed out"
    // warning when it is slow or absent, so those callers pass false.
    // Never throws; null when the command could not be sent or went unanswered.
    public async Task<Payload?> SendCommandAsync(DiscordCommandEnum command, object? args = null,
        DiscordEventEnum? discordEvent = null, bool waitForResponse = true)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(20));
        TaskCompletionSource<Payload?>? response = null;

        try
        {
            if (waitForResponse)
            {
                response = new TaskCompletionSource<Payload?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingCommands[nonce] = response;
            }

            var request = JsonSerializer.Serialize(new
            {
                nonce,
                cmd = (string) command,
                evt = discordEvent != null ? (string) discordEvent : "",
                args = args ?? ""
            }, DiscordJson.Options);

            Logger.LogTrace("Command request: {Request}", request);

            if (!await WriteFrameAsync(Opcode.Frame, Encoding.UTF8.GetBytes(request)))
            {
                Logger.LogWarning("Could not send {Command}: not connected to Discord", (string) command);
                return null;
            }

            if (response == null)
                return null;

            // A lost reply gives up instead of hanging this await forever.
            return await response.Task.WaitAsync(CommandTimeout);
        }
        catch (TimeoutException)
        {
            Logger.LogWarning("Command {Command} timed out waiting for a response", (string) command);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Command {Command} failed", (string) command);
            return null;
        }
        finally
        {
            _pendingCommands.TryRemove(nonce, out _);
        }
    }

    protected override async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        var pipe = await ConnectPipeAsync(cancellationToken)
            ?? throw new IOException("No Discord IPC pipe accepted the connection");

        _pipe = pipe;

        try
        {
            var handshake = JsonSerializer.SerializeToUtf8Bytes(new { v = HandshakeVersion, client_id = _clientId },
                DiscordJson.Options);

            if (!await WriteFrameAsync(Opcode.Handshake, handshake))
                throw new IOException("Failed to send the Discord IPC handshake");

            var loginStarted = false;

            while (true)
            {
                var (opcode, data) = await ReadFrameAsync(pipe, cancellationToken);

                switch (opcode)
                {
                    case Opcode.Frame:
                        if (HandleFrame(data) == (string) DiscordEventEnum.Ready && !loginStarted)
                        {
                            loginStarted = true;

                            // Runs on its own: the login waits for responses
                            // that only this loop can deliver.
                            _ = LoginAsync(cancellationToken);
                        }
                        break;

                    case Opcode.Ping:
                        await WriteFrameAsync(Opcode.Pong, data);
                        break;

                    case Opcode.Close:
                        Logger.LogDebug("Discord closed the connection: {Message}", Encoding.UTF8.GetString(data));
                        return;
                }
            }
        }
        finally
        {
            _pipe = null;
            await pipe.DisposeAsync();
        }
    }

    // Waiting on a response the connection can no longer deliver is a hang;
    // release the callers instead.
    protected override void OnDisconnected(bool wasConnected)
    {
        foreach (var nonce in _pendingCommands.Keys)
        {
            if (_pendingCommands.TryRemove(nonce, out var pending))
                pending.TrySetResult(null);
        }
    }

    private async Task<NamedPipeClientStream?> ConnectPipeAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < PipeCount; i++)
        {
            var pipeName = GetPipeName(i);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(PipeConnectTimeoutMs, cancellationToken);
                Logger.LogDebug("Connected to {PipeName}", pipeName);
                return pipe;
            }
            // Expected while Discord is still starting, and for every pipe
            // number it does not use.
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Logger.LogTrace("Failed to connect to {PipeName}: {Message}", pipeName, ex.Message);
                await pipe.DisposeAsync();
            }
            catch
            {
                await pipe.DisposeAsync();
                throw;
            }
        }

        return null;
    }

    // A named pipe on Windows. Elsewhere Discord listens on a Unix socket in
    // the first temp directory set, the order its own client libraries use;
    // NamedPipeClientStream takes a rooted name there as the socket path.
    internal static string GetPipeName(int index)
    {
        var name = $"discord-ipc-{index}";

        if (OperatingSystem.IsWindows())
            return name;

        var directory = new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" }
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? "/tmp";

        return Path.Combine(directory, name);
    }

    private static async Task<(Opcode Opcode, byte[] Data)> ReadFrameAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var header = new byte[8];

        // Throws EndOfStreamException when Discord goes away.
        await pipe.ReadExactlyAsync(header, cancellationToken);

        var opcode = (Opcode) BinaryPrimitives.ReadUInt32LittleEndian(header);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));

        if (length > MaxFrameSize)
            throw new InvalidDataException($"Discord sent a frame of {length} bytes");

        var data = new byte[length];
        await pipe.ReadExactlyAsync(data, cancellationToken);

        return (opcode, data);
    }

    // One write at a time — commands are sent from arbitrary threads. Returns
    // false when there is no open pipe to write to.
    private async Task<bool> WriteFrameAsync(Opcode opcode, byte[] data)
    {
        var pipe = _pipe;

        if (pipe is not { IsConnected: true })
            return false;

        var frame = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint) opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint) data.Length);
        data.CopyTo(frame, 8);

        await _writeLock.WaitAsync();

        try
        {
            await pipe.WriteAsync(frame);
            await pipe.FlushAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            Logger.LogDebug("Failed to write a {Opcode} frame: {Message}", opcode, ex.Message);
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // Resolves a command response or routes an event. Returns the dispatched
    // event, so the loop can start the login on READY.
    private string? HandleFrame(byte[] data)
    {
        Payload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<Payload>(data, DiscordJson.Options);
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse a Discord frame");
            return null;
        }

        if (payload == null)
            return null;

        Logger.LogTrace("Frame: {Frame}", Encoding.UTF8.GetString(data));

        if (payload.Nonce != null && _pendingCommands.TryRemove(payload.Nonce, out var pending))
        {
            pending.TrySetResult(payload);
            return null;
        }

        // Command acknowledgements nobody waits for (SUBSCRIBE) come back
        // without an event; only DISPATCH frames carry one to route.
        if (payload.Cmd != (string) DiscordCommandEnum.Dispatch || string.IsNullOrEmpty(payload.Evt))
            return null;

        // Events are only routed once the client is authenticated and has
        // subscribed to them.
        if (IsConnected)
            RouteEvent(payload);

        return payload.Evt;
    }

    private void RouteEvent(Payload payload)
    {
        try
        {
            if (payload.Evt == (string) DiscordEventEnum.VoiceChannelSelect)
            {
                if (payload.DataAs<ChannelSelectResponse>() is { } channel)
                    VoiceChannelSelected?.Invoke(channel);
            }
            else if (payload.Evt == (string) DiscordEventEnum.VoiceSettingsUpdate)
            {
                if (payload.DataAs<VoiceSettingsResponse>() is { } settings)
                    VoiceSettingsUpdated?.Invoke(settings);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to route the {Event} event", payload.Evt);
        }
    }

    // Never throws: it runs detached from the connection loop, so anything it
    // does not catch would surface as an unobserved exception.
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetCurrentTokenAsync();

            if (token == null || !token.Scopes.SequenceEqual(ScopesRequired))
            {
                token = await GetNewTokenAsync();
                await _storage.SetAsync(StorageTokenKey, token);
            }

            if (token == null)
                return;

            var response = await AuthenticateAsync(token.Token);

            // The stored token was revoked or expired early: authorize again.
            if (response == null && !cancellationToken.IsCancellationRequested)
            {
                token = await GetNewTokenAsync();

                if (token != null)
                {
                    await _storage.SetAsync(StorageTokenKey, token);
                    response = await AuthenticateAsync(token.Token);
                }
            }

            if (response == null)
            {
                if (!cancellationToken.IsCancellationRequested)
                    Logger.LogWarning("Discord did not accept the authentication");

                return;
            }

            // Ignored by the base when this connection has already ended.
            MarkConnected();

            // Fire the subscriptions independently: a stalled ack for one
            // must not stop the other from being sent, otherwise the mute /
            // deafen feedback silently never subscribes.
            await Task.WhenAll(
                SubscribeAsync(DiscordEventEnum.VoiceChannelSelect),
                SubscribeAsync(DiscordEventEnum.VoiceSettingsUpdate));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Discord login failed");
        }
    }

    private async Task<AuthToken?> GetCurrentTokenAsync()
    {
        var authToken = await _storage.GetAsync<AuthToken?>(StorageTokenKey, null);

        if (authToken == null)
            return null;

        // Still valid with a safety margin — use it as is.
        if (authToken.Expires.Subtract(TimeSpan.FromHours(TokenRefreshBufferHours)) > DateTime.Now)
            return authToken;

        return await RenewTokenAsync(authToken);
    }

    private async Task<AuthToken?> GetNewTokenAsync()
    {
        string? code;

        try
        {
            code = await AuthorizeAsync();
        }
        catch (AuthRejectedException)
        {
            Logger.LogWarning("The user rejected the Discord authorization");
            return null;
        }

        if (code == null)
            return null;

        var tokenResponse = await RequestTokenAsync("authorization_code", "code", code);
        return tokenResponse == null ? null : AuthTokenHelper.CreateFrom(ScopesRequired, tokenResponse);
    }

    private async Task<AuthToken?> RenewTokenAsync(AuthToken oldToken)
    {
        var tokenResponse = await RequestTokenAsync("refresh_token", "refresh_token", oldToken.RefreshToken);
        return tokenResponse == null ? null : AuthTokenHelper.CreateFrom(ScopesRequired, tokenResponse);
    }

    private async Task<string?> AuthorizeAsync()
    {
        var payload = await SendCommandAsync(DiscordCommandEnum.Authorize, new
        {
            scopes = string.Join(" ", ScopesRequired),
            client_id = _clientId
        });

        if (payload == null)
            return null;

        // An error carries {"code": <number>, "message": "..."} in place of
        // the authorization code.
        if (payload.IsError)
        {
            if (payload.Data is { ValueKind: JsonValueKind.Object } data
                && data.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.GetInt32() == AuthRejectedCode)
                throw new AuthRejectedException("User rejected authorization");

            Logger.LogWarning("Discord refused the authorization: {Message}", payload.GetDataString("message"));
            return null;
        }

        return payload.DataAs<AuthorizeResponse>()?.Code;
    }

    // Exchanges an authorization code or a refresh token for an access token;
    // the two OAuth grants differ only in the grant type and its credential.
    private async Task<TokenResponse?> RequestTokenAsync(string grantType, string credentialName, string credential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("grant_type", grantType),
                new KeyValuePair<string, string>(credentialName, credential),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret)
            ])
        };

        try
        {
            using var response = await Http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                // A refresh token Discord no longer accepts is expected after
                // a long pause; the login then authorizes again on its own.
                Logger.Log(grantType == "refresh_token" ? LogLevel.Warning : LogLevel.Error,
                    "Discord token request ({GrantType}) failed with {StatusCode}",
                    grantType, (int) response.StatusCode);
                return null;
            }

            await using var content = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TokenResponse>(content, DiscordJson.Options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Discord token request ({GrantType}) failed", grantType);
            return null;
        }
    }

    private async Task<AuthenticateResponse?> AuthenticateAsync(string token)
    {
        var payload = await SendCommandAsync(DiscordCommandEnum.Authenticate, new
        {
            access_token = token
        });

        var authResponse = payload?.DataAs<AuthenticateResponse>();

        if (authResponse?.User == null || authResponse.Expires == null || authResponse.Application == null || authResponse.Scopes == null)
            return null;

        return authResponse;
    }

    private Task<Payload?> SubscribeAsync(DiscordEventEnum discordEvent)
    {
        return SendCommandAsync(DiscordCommandEnum.Subscribe, null, discordEvent, waitForResponse: false);
    }
}
