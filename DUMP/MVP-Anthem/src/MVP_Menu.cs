using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using Menu;
using Menu.Enums;
using static MVPAnthem.MVPAnthem;

namespace MVPAnthem;

public static class MVPMenu
{
    public static void Display(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return;

        var localizer = Instance.Localizer;
        var (currentMvpName, _) = Instance.PlayerCache.GetMVP(player);

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, Action>();
        int i = 0;

        if (!string.IsNullOrEmpty(currentMvpName))
        {
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(localizer["mvp<remove>"])]));
            optionMap[i++] = () => ShowRemoveConfirmation(player);
        }

        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(localizer["mvp<option>"])]));
        optionMap[i++] = () => ShowCategoryMenu(player);

        var title = !string.IsNullOrEmpty(currentMvpName)
            ? $"{localizer["mvp<mainmenu>"]} - {currentMvpName}"
            : localizer["mvp<mainmenu>"];

        ShowMenu(player, title, items, optionMap, isSubMenu: false);
    }

    private static void ShowRemoveConfirmation(CCSPlayerController player)
    {
        var localizer = Instance.Localizer;
        List<MenuItem> items =
        [
            new MenuItem(MenuItemType.Button, [new MenuValue(localizer["remove<yes>"])]),
            new MenuItem(MenuItemType.Button, [new MenuValue(localizer["remove<no>"])])
        ];

        Instance.Menu?.ShowScrollableMenu(player, localizer["mvp<remove.confirm>"], items,
            (buttons, menu, selected) =>
            {
                if (selected == null) return;
                if (buttons == MenuButtons.Select && menu.Option == 0)
                {
                    Instance.PlayerCache.RemoveMVP(player);
                    player.PrintToChat(localizer["prefix"] + localizer["mvp.removed"]);
                }
            }, false, freezePlayer: false, disableDeveloper: true);
    }

    private static void ShowCategoryMenu(CCSPlayerController player)
    {
        var accessible = new List<(string name, CategorySettings settings)>();

        foreach (var cat in Instance.MVPSettings.MVPSettings)
        {
            if (!HasCategoryAccess(player, cat.Value.CategoryFlags)) continue;
            if (cat.Value.MVPs.Any(m => ValidatePlayerForMVP(player, m.Value)))
                accessible.Add((cat.Key, cat.Value));
        }

        if (accessible.Count == 0)
        {
            player.PrintToChat(Instance.Localizer["prefix"] + Instance.Localizer["mvp<unavailable>"]);
            return;
        }

        if (accessible.Count == 1)
        {
            ShowMVPList(player, accessible[0].name, accessible[0].settings);
            return;
        }

        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, Action>();
        for (int i = 0; i < accessible.Count; i++)
        {
            var (name, settings) = accessible[i];
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(name)]));
            optionMap[i] = () => ShowMVPList(player, name, settings);
        }

        ShowMenu(player, Instance.Localizer["categories<menu>"], items, optionMap, isSubMenu: true);
    }

    private static void ShowMVPList(CCSPlayerController player, string categoryName, CategorySettings category)
    {
        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, Action>();
        int i = 0;

        foreach (var mvp in category.MVPs)
        {
            if (!ValidatePlayerForMVP(player, mvp.Value)) continue;
            var settings = mvp.Value;
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(settings.MVPName)]));
            optionMap[i++] = () => ShowMVPActions(player, settings);
        }

        if (i == 0)
        {
            player.PrintToChat(Instance.Localizer["prefix"] + Instance.Localizer["mvp<unavailable>"]);
            return;
        }

        ShowMenu(player, categoryName, items, optionMap, isSubMenu: true);
    }

    private static void ShowMVPActions(CCSPlayerController player, MVP_Settings mvp)
    {
        var localizer = Instance.Localizer;
        List<MenuItem> items = new();
        var optionMap = new Dictionary<int, Action>();
        int i = 0;

        items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(localizer["equip<yes>"])]));
        optionMap[i++] = () =>
        {
            Instance.PlayerCache.SetMVP(player, mvp.MVPName, mvp.MVPSound);
            player.PrintToChat(localizer["prefix"] + localizer["mvp.equipped", mvp.MVPName]);
        };

        if (mvp.EnablePreview)
        {
            items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(localizer["preview<option>"])]));
            optionMap[i++] = () =>
            {
                Events.PlayPreviewToPlayer(player, mvp);
                player.PrintToChat(localizer["prefix"] + localizer["mvp.previewed", mvp.MVPName]);
            };
        }

        ShowMenu(player, localizer["mvp<equip>", mvp.MVPName], items, optionMap, isSubMenu: true);
    }

    private static void ShowMenu(CCSPlayerController player, string title,
        List<MenuItem> items, Dictionary<int, Action> optionMap, bool isSubMenu = false)
    {
        Instance.Menu?.ShowScrollableMenu(player, title, items,
            (buttons, menu, selected) =>
            {
                if (selected == null) return;
                if (buttons == MenuButtons.Select && optionMap.TryGetValue(menu.Option, out var action))
                    action.Invoke();
            }, isSubMenu, freezePlayer: false, disableDeveloper: true);
    }

    private static bool HasCategoryAccess(CCSPlayerController player, List<string> flags)
    {
        if (flags == null || flags.Count == 0) return true;
        return flags.Any(f =>
            (f.StartsWith("#") && AdminManager.PlayerInGroup(player, f)) ||
            (f.StartsWith("@") && AdminManager.PlayerHasPermissions(player, f)));
    }

    private static bool ValidatePlayerForMVP(CCSPlayerController player, MVP_Settings settings)
    {
        if (!string.IsNullOrEmpty(settings.SteamID) && player.SteamID.ToString() == settings.SteamID)
            return true;

        if (settings.Flags.Count > 0)
            return settings.Flags.Any(f =>
                (f.StartsWith("#") && AdminManager.PlayerInGroup(player, f)) ||
                (f.StartsWith("@") && AdminManager.PlayerHasPermissions(player, f)));

        return string.IsNullOrEmpty(settings.SteamID);
    }
}
