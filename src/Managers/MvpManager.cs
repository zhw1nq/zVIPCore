using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace zVIPCore;

public class MvpManager
{
    private Timer? _centerHtmlTimer;
    private Timer? _centerHtmlTickTimer;
    private bool _isCenterHtmlActive;
    private string _htmlMessage = "";

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
    public static void PlayPreviewToPlayer(CCSPlayerController player, MvpItemSettings mvpSettings)
    {
        if (!player.IsValid) return;
        player.EmitSound(mvpSettings.MVPSound, player, 1.0f);
    }

    private static string? GetLocalizedMessage(string mvpKey, string messageType)
    {
        var localizer = zVIPCore.Instance.Localizer;

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

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isCenterHtmlActive = false;
        _centerHtmlTimer?.Kill();
        _centerHtmlTimer = null;
        _centerHtmlTickTimer?.Kill();
        _centerHtmlTickTimer = null;

        return HookResult.Continue;
    }

    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        int slot = player.Slot;
        zVIPCore.Instance.AddTimer(3.0f, () =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p == null || !p.IsValid || p.Connected != PlayerConnectedState.PlayerConnected) return;
            _ = Task.Run(async () =>
            {
                await Database.GetPlayerMvpAsync(p.SteamID, "CT");
                await Database.GetPlayerMvpAsync(p.SteamID, "T");
            });
        });

        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;

        ulong steamId = player.SteamID;
        _ = Task.Run(async () =>
        {
            await Database.FlushMvpAsync(steamId);
            Server.NextFrame(() => Database.ClearMvpPlayer(steamId));
        });

        return HookResult.Continue;
    }

    /// <summary>
    /// Map team number to DB team string.
    /// TeamNum: 2 = Terrorist, 3 = Counter-Terrorist
    /// </summary>
    private static string GetTeamString(int teamNum) => teamNum == 3 ? "CT" : "T";

    public HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        var mvpPlayer = @event.Userid;
        if (mvpPlayer == null || !mvpPlayer.IsValid) return HookResult.Continue;

        if (Config.Mvp.DisableDefaultMvp)
            mvpPlayer.MVPs = 0;

        // Determine team of the MVP player
        var teamNum = mvpPlayer.TeamNum;
        var team = GetTeamString(teamNum);

        var (mvpName, mvpSound) = Database.GetMvpFromCache(mvpPlayer.SteamID, team);

        // Fallback: if no MVP for current team, try the other team
        if (string.IsNullOrEmpty(mvpSound) || string.IsNullOrEmpty(mvpName))
        {
            var otherTeam = team == "CT" ? "T" : "CT";
            var (fallbackName, fallbackSound) = Database.GetMvpFromCache(mvpPlayer.SteamID, otherTeam);
        }

        if (string.IsNullOrEmpty(mvpSound) || string.IsNullOrEmpty(mvpName))
            return HookResult.Continue;

        info.DontBroadcast = true;

        MvpItemSettings? mvpSettings = null;
        string? mvpKey = null;

        foreach (var cat in zVIPCore.MvpSettings.MVPSettings)
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

        var localizer = zVIPCore.Instance.Localizer;
        var timer = Config.Mvp;

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
                    p.PrintToChat(localizer["zVIPCore.prefix"] + string.Format(msg, mvpPlayer.PlayerName, mvpSettings.MVPName));
            }
        }

        // Setup center HTML display (once, not per-player)
        if (htmlMsg != null)
        {
            _htmlMessage = htmlMsg;
            _isCenterHtmlActive = true;
            _centerHtmlTimer?.Kill();
            _centerHtmlTimer = zVIPCore.Instance.AddTimer(timer.CenterHtmlDuration, () =>
            {
                _isCenterHtmlActive = false;
                _centerHtmlTimer = null;
                _centerHtmlTickTimer?.Kill();
                _centerHtmlTickTimer = null;
            });

            _centerHtmlTickTimer?.Kill();
            _centerHtmlTickTimer = zVIPCore.Instance.AddTimer(0.1f, () =>
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


    public HookResult OnMapEnd(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        Task.Run(async () =>
        {
            int dirty = Database.GetMvpDirtyCount();
            if (dirty > 0)
            {
                Console.WriteLine($"[zVIPCore] Flushing {dirty} MVP preferences to database...");
                await Database.FlushAllMvpAsync();
            }
            Server.NextFrame(() => Database.ClearMvpAll());
        });

        return HookResult.Continue;
    }

    public void Dispose()
    {
        _centerHtmlTimer?.Kill();
        _centerHtmlTimer = null;
        _centerHtmlTickTimer?.Kill();
        _centerHtmlTickTimer = null;
    }

    // Shorthand references
    private static Config Config => zVIPCore.Config;
    private static Database Database => zVIPCore.Database;
}
