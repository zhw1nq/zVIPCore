using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using static MVPAnthem.MVPAnthem;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace MVPAnthem;

public static class Events
{
    private static Timer? _centerHtmlTimer;
    private static Timer? _centerHtmlTickTimer;
    private static bool _isCenterHtmlActive;
    private static string _htmlMessage = "";

    /// <summary>
    /// Emit MVP sound on every human player (self-to-self).
    /// Sound is non-positional via vsndevts settings (use_hrtf=0, distance_max=100000).
    /// Called ONCE — no need to re-emit.
    /// </summary>
    public static void EmitSoundOnAllPlayers(string soundName)
    {
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
            p.EmitSound(soundName, p, 1.0f);
    }

    /// <summary>
    /// Preview: emit sound on a single player (self-to-self), one time only.
    /// </summary>
    public static void PlayPreviewToPlayer(CCSPlayerController player, MVP_Settings mvpSettings)
    {
        if (!player.IsValid) return;
        player.EmitSound(mvpSettings.MVPSound, player, 1.0f);
    }

    public static void RegisterEvents()
    {
        Instance.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
        Instance.RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Pre);
        Instance.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        Instance.RegisterEventHandler<EventCsWinPanelMatch>(OnMapEnd);
        Instance.RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    public static void Dispose()
    {
        _centerHtmlTimer?.Kill();
        _centerHtmlTimer = null;
        _centerHtmlTickTimer?.Kill();
        _centerHtmlTickTimer = null;
    }

    private static string? GetLocalizedMessage(string mvpKey, string messageType)
    {
        var localizer = Instance.Localizer;

        var specificKey = $"{mvpKey}.{messageType}";
        var msg = localizer[specificKey];
        if (!string.IsNullOrEmpty(msg) && msg != specificKey)
            return msg;

        var defaultKey = $"mvp.default.{messageType}";
        var defaultMsg = localizer[defaultKey];
        if (!string.IsNullOrEmpty(defaultMsg) && defaultMsg != defaultKey)
            return defaultMsg;

        return null;
    }

    private static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isCenterHtmlActive = false;
        _centerHtmlTimer?.Kill();
        _centerHtmlTimer = null;
        _centerHtmlTickTimer?.Kill();
        _centerHtmlTickTimer = null;

        return HookResult.Continue;
    }

    private static HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        int slot = player.Slot;
        Instance.AddTimer(3.0f, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p == null || !p.IsValid || p.Connected != PlayerConnectedState.PlayerConnected) return;
            _ = Task.Run(async () => await Instance.PlayerCache.GetPlayerDataAsync(p));
        });

        return HookResult.Continue;
    }

    private static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        ulong steamId = player.SteamID;
        _ = Task.Run(async () =>
        {
            await Instance.PlayerCache.FlushPlayerAsync(steamId);
            Server.NextFrame(() => Instance.PlayerCache.RemovePlayer(steamId));
        });

        return HookResult.Continue;
    }

    private static HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        var mvpPlayer = @event.Userid;
        if (mvpPlayer == null || !mvpPlayer.IsValid) return HookResult.Continue;

        if (Instance.Config.Settings.DisablePlayerDefaultMVP)
            mvpPlayer.MVPs = 0;

        var (mvpName, mvpSound) = Instance.PlayerCache.GetMVP(mvpPlayer);
        if (string.IsNullOrEmpty(mvpSound) || string.IsNullOrEmpty(mvpName))
            return HookResult.Continue;

        info.DontBroadcast = true;

        MVP_Settings? mvpSettings = null;
        string? mvpKey = null;

        foreach (var cat in Instance.MVPSettings.MVPSettings)
        {
            foreach (var entry in cat.Value.MVPs)
            {
                if (entry.Value.MVPName == mvpName && entry.Value.MVPSound == mvpSound)
                {
                    mvpSettings = entry.Value;
                    mvpKey = entry.Key;
                    break;
                }
            }
            if (mvpSettings != null) break;
        }

        if (mvpSettings == null || string.IsNullOrEmpty(mvpKey))
            return HookResult.Continue;

        var localizer = Instance.Localizer;
        var timer = Instance.Config.Timer;

        // Emit sound ONCE on every player (self-to-self)
        // Non-positional via vsndevts: use_hrtf=0, distance_max=100000
        EmitSoundOnAllPlayers(mvpSound);

        // Prepare HTML message once (outside player loop)
        string? htmlMsg = null;
        if (mvpSettings.ShowHtmlMessage)
        {
            htmlMsg = GetLocalizedMessage(mvpKey, "html");
            if (htmlMsg != null)
                htmlMsg = string.Format(htmlMsg, mvpPlayer.PlayerName, mvpSettings.MVPName);
        }

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
        {
            if (mvpSettings.ShowChatMessage)
            {
                var msg = GetLocalizedMessage(mvpKey, "chat");
                if (msg != null)
                    p.PrintToChat(localizer["prefix"] + string.Format(msg, mvpPlayer.PlayerName, mvpSettings.MVPName));
            }
        }

        // Setup center HTML display (once, not per-player)
        if (htmlMsg != null)
        {
            _htmlMessage = htmlMsg;
            _isCenterHtmlActive = true;
            _centerHtmlTimer?.Kill();
            _centerHtmlTimer = Instance.AddTimer(timer.CenterHtmlDuration, () =>
            {
                _isCenterHtmlActive = false;
                _centerHtmlTimer = null;
                _centerHtmlTickTimer?.Kill();
                _centerHtmlTickTimer = null;
            });

            _centerHtmlTickTimer?.Kill();
            _centerHtmlTickTimer = Instance.AddTimer(0.1f, () =>
            {
                if (!_isCenterHtmlActive)
                {
                    _centerHtmlTickTimer?.Kill();
                    _centerHtmlTickTimer = null;
                    return;
                }

                foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
                    p.PrintToCenterHtml($"{_htmlMessage}</div>");
            }, TimerFlags.REPEAT);
        }

        return HookResult.Continue;
    }

    private static HookResult OnMapEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        Task.Run(async () =>
        {
            int dirty = Instance.PlayerCache.GetDirtyCount();
            if (dirty > 0)
            {
                Instance.Logger.LogInformation($"[MVP-Anthem] Flushing {dirty} preferences to database...");
                await Instance.PlayerCache.FlushAllAsync();
            }
            Server.NextFrame(() => Instance.PlayerCache.ClearAll());
        });

        return HookResult.Continue;
    }
}
