using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace SanctumPresets;

/// <summary> What the controller needs from Penumbra (lets the logic be tested without the game). </summary>
public interface IPenumbra
{
    string LastError { get; }
    bool IsAvailable(out string reason);
    List<(string Directory, string Name)> GetMods();
    IReadOnlyDictionary<string, (string[] Options, int Type)>? GetGroups(string modDirectory);
    (Guid Id, string Name)? GetPlayerCollection();
    (bool Enabled, Dictionary<string, List<string>> Settings)? GetCurrent(Guid collection, string modDirectory);
    bool SetEnabled(Guid collection, string modDirectory, bool enabled);
    bool SetOption(Guid collection, string modDirectory, string group, string option);
    bool SetOptions(Guid collection, string modDirectory, string group, IReadOnlyList<string> options);
    void RedrawPlayer();
}

/// <summary>
/// Talks to Penumbra through its public IPC (API version 5).
/// Labels and argument orders match Penumbra.Api as used by Penumbra 1.7.2.1.
/// </summary>
public sealed class PenumbraBridge : IPenumbra
{
    public const int RequiredBreakingVersion = 5;

    private readonly ICallGateSubscriber<(int Breaking, int Features)> apiVersion;
    private readonly ICallGateSubscriber<Dictionary<string, string>> getModList;
    private readonly ICallGateSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?> getAvailable;
    private readonly ICallGateSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)> getCurrent;
    private readonly ICallGateSubscriber<Guid, string, string, string, string, int> trySetSetting;      // collection, modDir, modName, group, option
    private readonly ICallGateSubscriber<Guid, string, string, string, IReadOnlyList<string>, int> trySetSettings; // collection, modDir, modName, group, options
    private readonly ICallGateSubscriber<Guid, string, string, bool, int> trySetMod;                     // collection, modDir, modName, enabled
    private readonly ICallGateSubscriber<int, (bool, bool, (Guid, string))> getCollectionForObject;
    private readonly ICallGateSubscriber<int, int, object> redrawObject;                                 // objectIndex, redrawType

    public string LastError { get; private set; } = string.Empty;

    public PenumbraBridge(IDalamudPluginInterface pi)
    {
        apiVersion             = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
        getModList             = pi.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        getAvailable           = pi.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?>("Penumbra.GetAvailableModSettings.V5");
        getCurrent             = pi.GetIpcSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)>("Penumbra.GetCurrentModSettings.V5");
        trySetSetting          = pi.GetIpcSubscriber<Guid, string, string, string, string, int>("Penumbra.TrySetModSetting.V5");
        trySetSettings         = pi.GetIpcSubscriber<Guid, string, string, string, IReadOnlyList<string>, int>("Penumbra.TrySetModSettings.V5");
        trySetMod              = pi.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TrySetMod.V5");
        getCollectionForObject = pi.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5");
        redrawObject           = pi.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5");
    }

    /// <summary> True if Penumbra is loaded and speaks a compatible API. </summary>
    public bool IsAvailable(out string reason)
    {
        try
        {
            var (breaking, _) = apiVersion.InvokeFunc();
            if (breaking != RequiredBreakingVersion)
            {
                reason = $"Penumbra API version {breaking} is not supported (needs {RequiredBreakingVersion}). Update this plugin or Penumbra.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception)
        {
            reason = "Penumbra is not loaded or not ready yet.";
            return false;
        }
    }

    /// <summary> All installed mods as (directory, display name). </summary>
    public List<(string Directory, string Name)> GetMods()
    {
        try
        {
            return getModList.InvokeFunc().Select(kv => (kv.Key, kv.Value)).OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return new();
        }
    }

    /// <summary> Option groups of a mod: group name -> (option names, group type: 0 = single, 1 = multi, ...). </summary>
    public IReadOnlyDictionary<string, (string[] Options, int Type)>? GetGroups(string modDirectory)
    {
        try
        {
            var raw = getAvailable.InvokeFunc(modDirectory, string.Empty);
            return raw?.ToDictionary(kv => kv.Key, kv => (kv.Value.Item1, kv.Value.Item2));
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return null;
        }
    }

    /// <summary> The collection Penumbra currently uses for the local player (object index 0). </summary>
    public (Guid Id, string Name)? GetPlayerCollection()
    {
        try
        {
            var (valid, _, effective) = getCollectionForObject.InvokeFunc(0);
            return valid ? effective : null;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return null;
        }
    }

    /// <summary> Current settings of the mod in a collection: (enabled, group -> selected options). </summary>
    public (bool Enabled, Dictionary<string, List<string>> Settings)? GetCurrent(Guid collection, string modDirectory)
    {
        try
        {
            var (ec, data) = getCurrent.InvokeFunc(collection, modDirectory, string.Empty, false);
            if (ec != 0 || data is null)
            {
                if (ec != 0) LastError = Describe(ec);
                return null;
            }

            var (enabled, _, settings, _) = data.Value;
            return (enabled, settings);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return null;
        }
    }

    public bool SetEnabled(Guid collection, string modDirectory, bool enabled)
        => Check(() => trySetMod.InvokeFunc(collection, modDirectory, string.Empty, enabled));

    /// <summary> Select one option in a single-choice group. </summary>
    public bool SetOption(Guid collection, string modDirectory, string group, string option)
        => Check(() => trySetSetting.InvokeFunc(collection, modDirectory, string.Empty, group, option));

    /// <summary> Select any number of options in a multi-choice group. </summary>
    public bool SetOptions(Guid collection, string modDirectory, string group, IReadOnlyList<string> options)
        => Check(() => trySetSettings.InvokeFunc(collection, modDirectory, string.Empty, group, options));

    /// <summary> Redraw the local player so new effects show up. </summary>
    public void RedrawPlayer()
    {
        try
        {
            redrawObject.InvokeAction(0, 0);
        }
        catch (Exception e)
        {
            LastError = e.Message;
        }
    }

    private bool Check(Func<int> call)
    {
        try
        {
            var ec = call();
            if (ec is 0 or 1) // Success or NothingChanged
                return true;

            LastError = Describe(ec);
            return false;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return false;
        }
    }

    public static string Describe(int ec)
        => ec switch
        {
            0  => "Success",
            1  => "Nothing changed",
            2  => "Collection not found",
            3  => "Mod not found in Penumbra",
            4  => "Option group not found (the mod may have changed)",
            5  => "Option not found (the mod may have changed)",
            11 => "Invalid argument",
            17 => "Penumbra is shutting down",
            22 => "Collection is not active",
            _  => $"Penumbra error {ec}",
        };
}
