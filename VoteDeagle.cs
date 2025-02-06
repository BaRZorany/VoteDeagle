using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using T3MenuSharedApi;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Reflection;

public class DeagleVotePlugin : BasePlugin
{
    public override string ModuleName => "Deagle Vote Plugin";
    public override string ModuleVersion => "2.3";
    public override string ModuleAuthor => "Your Name";

    private bool _isDeagleOnlyEnabled = false;
    private Dictionary<int, bool> _playerVotes = new Dictionary<int, bool>();

    private Dictionary<string, string> _translations = new Dictionary<string, string>();
    public IT3MenuManager? MenuManager;

    public IT3MenuManager? GetMenuManager()
    {
        if (MenuManager == null)
            MenuManager = new PluginCapability<IT3MenuManager>("t3menu:manager").Get();
        return MenuManager;
    }

    public override void Load(bool hotReload)
    {
        LoadTranslations("en");
        AddCommand("css_vd", "Vote for deagle-only rounds", OnDeagleVoteCommand);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    private void LoadTranslations(string languageCode)
    {
        string langFilePath = Path.Join(ModuleDirectory, "lang", $"{languageCode}.json");
        if (!File.Exists(langFilePath)) return;

        try
        {
            string jsonContent = File.ReadAllText(langFilePath);
            _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent) ?? new Dictionary<string, string>();
        }
        catch { /* Handle error */ }
    }

    private string FormatMessage(string format, params object[] args)
    {
        for (int i = 0; i < args.Length; i++)
            format = format.Replace($"{{{i}}}", args[i]?.ToString() ?? string.Empty);
        return format;
    }

    private string Translate(string key, params object[] args)
    {
        if (_translations.TryGetValue(key, out string? value))
        {
            value = value.Replace("{{", "{").Replace("}}", "}")
                        .ReplaceColorTags();
            return FormatMessage(value, args);
        }
        return $"Missing translation: {key}";
    }

    private void PrintToChatAll(string key, params object[] args)
    {
        string prefix = _translations.TryGetValue("prefix", out string? prefixValue)
            ? prefixValue.ReplaceColorTags() : "{LightBlue}[DeagleVote]{Default}".ReplaceColorTags();
        Server.PrintToChatAll($"{prefix} {Translate(key, args)}");
    }

    private void PrintToChat(CCSPlayerController player, string key, params object[] args)
    {
        string prefix = _translations.TryGetValue("prefix", out string? prefixValue)
            ? prefixValue.ReplaceColorTags() : "{LightBlue}[DeagleVote]{Default}".ReplaceColorTags();
        player.PrintToChat($"{prefix} {Translate(key, args)}");
    }

    [ConsoleCommand("css_vd")]
    public void OnDeagleVoteCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid) return;
        var manager = GetMenuManager();
        if (manager == null) return;

        // Use the appropriate title based on the current state
        string menuTitle = _isDeagleOnlyEnabled
            ? Translate("disable.menu.title")
            : Translate("enable.menu.title");

        var voteMenu = manager.CreateMenu(
            menuTitle,
            isSubMenu: false,
            freezePlayer: false
        );

        // Use translated "Yes" and "No" options
        voteMenu.Add(Translate("menu.yes"), (p, option) => HandleVote(player, true));
        voteMenu.Add(Translate("menu.no"), (p, option) => HandleVote(player, false));

        manager.OpenMainMenu(player, voteMenu);
    }

    private void HandleVote(CCSPlayerController player, bool voteYes)
    {
        var manager = GetMenuManager();
        manager?.CloseMenu(player);

        if (voteYes)
        {
            PrintToChatAll("vote.started", player.PlayerName);
            foreach (var otherPlayer in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p != player))
                OpenVoteMenuForPlayer(otherPlayer);
        }

        ProcessPlayerVote(player, voteYes);
    }

    private void ProcessPlayerVote(CCSPlayerController player, bool voteYes)
    {
        var playerIndex = (int)player.Index;
        if (_playerVotes.ContainsKey(playerIndex))
        {
            PrintToChat(player, "vote.already_voted");
            return;
        }

        _playerVotes[playerIndex] = voteYes;
        PrintToChat(player, voteYes ? "vote.yes" : "vote.no");

        if (_playerVotes.Count >= Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot))
            ProcessVoteResults();
    }

    private void ProcessVoteResults()
    {
        int yesVotes = _playerVotes.Count(v => v.Value);
        int totalPlayers = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot);

        if (yesVotes > totalPlayers / 2)
        {
            _isDeagleOnlyEnabled = !_isDeagleOnlyEnabled;
            PrintToChatAll(_isDeagleOnlyEnabled ? "vote.enabled_deagle" : "vote.disabled_deagle");
        }
        else
        {
            PrintToChatAll("vote.failed");
        }

        _playerVotes.Clear();
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_isDeagleOnlyEnabled)
        {
            Server.NextFrame(() =>
            {
                foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
                {
                    player.RemoveWeapons();
                    player.GiveNamedItem("weapon_deagle");
                    player.GiveNamedItem("weapon_knife");
                    PrintToChat(player, "game.deagle_only_active");
                }
            });
        }
        return HookResult.Continue;
    }

    private void OpenVoteMenuForPlayer(CCSPlayerController player)
    {
        var manager = GetMenuManager();
        if (manager == null) return;

        // Use the appropriate title based on the current state
        string menuTitle = _isDeagleOnlyEnabled
            ? Translate("disable.menu.title")
            : Translate("enable.menu.title");

        var voteMenu = manager.CreateMenu(
            menuTitle,
            isSubMenu: false,
            freezePlayer: false
        );

        // Use translated "Yes" and "No" options
        voteMenu.Add(Translate("menu.yes"), (p, option) => ProcessPlayerVote(player, true));
        voteMenu.Add(Translate("menu.no"), (p, option) => ProcessPlayerVote(player, false));

        manager.OpenMainMenu(player, voteMenu);
    }
}

public static class StringExtensions
{
    public static string ReplaceColorTags(this string message)
    {
        var modifiedValue = message;
        foreach (var field in typeof(ChatColors).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string pattern = $"{{{field.Name}}}";
            if (modifiedValue.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                modifiedValue = modifiedValue.Replace(
                    pattern, 
                    field.GetValue(null)?.ToString() ?? string.Empty, 
                    StringComparison.OrdinalIgnoreCase
                );
            }
        }
        return modifiedValue;
    }
}