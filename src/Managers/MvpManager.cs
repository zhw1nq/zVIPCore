using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using System.Linq;

namespace zVIPCore;

public class MvpManager
{
    private MvpModelsConfig _modelsConfig = new();
    private Timer? _centerHtmlTimer;
    private Timer? _centerHtmlTickTimer;
    private bool _isCenterHtmlActive;
    private string _htmlMessage = "";

    public void UpdateModelsConfig(MvpModelsConfig config) => _modelsConfig = config;

    public MvpModelsConfig GetModelsConfig() => _modelsConfig;

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isCenterHtmlActive = false;
        _centerHtmlTimer?.Kill();
        _centerHtmlTimer = null;
        _centerHtmlTickTimer?.Kill();
        _centerHtmlTickTimer = null;

        return HookResult.Continue;
    }

    public HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        var mvpPlayer = @event.Userid;
        if (mvpPlayer == null || !mvpPlayer.IsValid || mvpPlayer.IsBot) return HookResult.Continue;

        var team = mvpPlayer.TeamNum == (int)CsTeam.CounterTerrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        
        // Wait, Database fetch is async, but OnRoundMvp is synchronous? Let's check Cache, it is synchronous locally.
        _ = zVIPCore.SafeAsync(async () =>
        {
            var (mvpName, mvpSound) = await zVIPCore.Database.GetPlayerMvpAsync(mvpPlayer.SteamID, team);
            if (string.IsNullOrEmpty(mvpName) || string.IsNullOrEmpty(mvpSound))
                return;

            var mvpSettings = _modelsConfig.FindMvpBySoundAndName(mvpName, mvpSound);
            if (mvpSettings == null)
                return;

            Server.NextFrame(() =>
            {
                // Emit sound on all players (self-to-self, non-positional)
                foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
                    p.EmitSound(mvpSound, p, 1.0f);

                var localizer = zVIPCore.Instance.Localizer;
                string? htmlMsg = null;

                if (mvpSettings.ShowHtmlMessage)
                {
                    string htmlTemplate = localizer["zVIPCore.mvp.html.1"];
                    if (!string.IsNullOrEmpty(htmlTemplate))
                        htmlMsg = string.Format(htmlTemplate, mvpPlayer.PlayerName, mvpSettings.MvpName);
                }

                if (mvpSettings.ShowChatMessage)
                {
                    string chatTemplate = localizer["zVIPCore.mvp.chat.1"];
                    if (!string.IsNullOrEmpty(chatTemplate))
                    {
                        var msg = string.Format(chatTemplate, mvpPlayer.PlayerName, mvpSettings.MvpName);
                        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
                            p.PrintToChat(localizer["zVIPCore.prefix"] + msg);
                    }
                }

                if (htmlMsg != null)
                {
                    _htmlMessage = htmlMsg;
                    _isCenterHtmlActive = true;
                    
                    _centerHtmlTimer?.Kill();
                    _centerHtmlTimer = zVIPCore.Instance.AddTimer(zVIPCore.Config.CenterHtmlDuration, () =>
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
            });
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
}
