using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InstantTeleport.Windows;

namespace InstantTeleport;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
	private delegate int GetAdjustedCastTimeDelegate(uint actionType, uint actionId, byte applyProcs, nint outOptProc);

	private unsafe delegate void SetOpenTransitionDelegate(AtkUnitBase* addon, float duration, short offsetX, short offsetY, float scale);

	private const string CommandName = "/instanttp";

	private const string CastTimeSig = "48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 41 54 41 55 41 56 41 57 48 83 EC 40 4C 8B 3D ?? ?? ?? ?? 49 8B F1 41 0F B6 D8 8B FA";

	private const string SetOpenTransitionSig = "E8 ?? ?? ?? ?? F3 0F 10 0D ?? ?? ?? ?? 45 33 C9 F3 0F 59 0D ?? ?? ?? ??";

	private static readonly HashSet<string> TransitionAddonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_LocationTitle", "_AreaTitle", "_WideText", "FadeMiddle", "_FadeMiddle", "FadeBack", "_FadeBack", "FadeFront", "_FadeFront" };

	private Hook<GetAdjustedCastTimeDelegate>? getAdjustedCastTimeHook;

	private Hook<SetOpenTransitionDelegate>? setOpenTransitionHook;

	private bool isTeleporting;

	private DateTime teleportStartUtc;

	private uint sourceTerritoryTypeId;

	private const float TeleportTimeoutSeconds = 20f;

	[PluginService]
	internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

	[PluginService]
	internal static IPluginLog Log { get; private set; } = null!;

	[PluginService]
	internal static ICommandManager CommandManager { get; private set; } = null!;

	[PluginService]
	internal static ISigScanner SigScanner { get; private set; } = null!;

	[PluginService]
	internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

	[PluginService]
	internal static IFramework Framework { get; private set; } = null!;

	[PluginService]
	internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

	public Configuration Configuration { get; }

	public WindowSystem WindowSystem { get; } = new WindowSystem("InstantTeleport");

	private ConfigWindow ConfigWindow { get; }

	public Plugin()
	{
		Configuration = (PluginInterface.GetPluginConfig() as Configuration) ?? new Configuration();
		Configuration.Initialize(PluginInterface);
		ConfigWindow = new ConfigWindow(this);
		WindowSystem.AddWindow(ConfigWindow);
		CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
		{
			HelpMessage = "Instant Teleport settings and status."
		});
		InitializeHooks();
		Framework.Update += OnFrameworkUpdate;
		RegisterAddonListeners();
		PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
		PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
		Log.Information("Instant Teleport loaded. Cast={Cast} Transition={Transition}", Configuration.EnableInstantCast, Configuration.EnableTransitionSkip);
	}

	private unsafe void InitializeHooks()
	{
		try
		{
			nint num = SigScanner.ScanText(CastTimeSig);
			getAdjustedCastTimeHook = GameInteropProvider.HookFromAddress<GetAdjustedCastTimeDelegate>(num, GetAdjustedCastTimeDetour);
			getAdjustedCastTimeHook.Enable();
			Log.Information("GetAdjustedCastTime hook enabled at 0x{Address:X}", num);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Failed to initialize GetAdjustedCastTime hook.");
		}
		try
		{
			nint num2 = SigScanner.ScanText(SetOpenTransitionSig);
			setOpenTransitionHook = GameInteropProvider.HookFromAddress<SetOpenTransitionDelegate>(num2, SetOpenTransitionDetour);
			setOpenTransitionHook.Enable();
			Log.Information("SetOpenTransition hook enabled at 0x{Address:X}", num2);
		}
		catch (Exception ex2)
		{
			Log.Warning(ex2, "Failed to initialize SetOpenTransition hook. Delay override/addon suppression will still apply.");
		}
	}

	private unsafe int GetAdjustedCastTimeDetour(uint actionType, uint actionId, byte applyProcs, nint outOptProc)
	{
		if (Configuration.EnableInstantCast && actionType == 1 && (actionId == 5 || actionId == 6))
		{
			isTeleporting = true;
			teleportStartUtc = DateTime.UtcNow;
			GameMain* ptr = GameMain.Instance();
			sourceTerritoryTypeId = (ptr != null) ? ptr->CurrentTerritoryTypeId : 0u;
			if (Configuration.DebugLogging)
			{
				Log.Debug("Instant cast applied to {Action}.", (actionId == 5) ? "Teleport" : "Return");
			}
			return 0;
		}
		return getAdjustedCastTimeHook!.Original(actionType, actionId, applyProcs, outOptProc);
	}

	private unsafe void SetOpenTransitionDetour(AtkUnitBase* addon, float duration, short offsetX, short offsetY, float scale)
	{
		if (addon != null && isTeleporting && Configuration.EnableTransitionSkip && Configuration.EnableFastFade && TransitionAddonNames.Contains(addon->NameString))
		{
			duration = Configuration.FadeDurationOverride;
		}
		setOpenTransitionHook!.Original(addon, duration, offsetX, offsetY, scale);
	}

	private unsafe void OnFrameworkUpdate(IFramework framework)
	{
		if (!isTeleporting)
		{
			return;
		}
		if ((DateTime.UtcNow - teleportStartUtc).TotalSeconds >= 20.0)
		{
			isTeleporting = false;
			return;
		}
		GameMain* ptr = GameMain.Instance();
		if (ptr != null)
		{
			if (Configuration.EnableTransitionSkip && ptr->TerritoryTransitionState != 0 && ptr->TerritoryTransitionDelay > Configuration.FadeDurationOverride)
			{
				ptr->TerritoryTransitionDelay = Configuration.FadeDurationOverride;
			}
			if (ptr->ConnectedToZone && ptr->CurrentTerritoryTypeId != 0 && ptr->CurrentTerritoryTypeId != sourceTerritoryTypeId && ptr->TerritoryTransitionState == 0)
			{
				isTeleporting = false;
			}
		}
	}

	private unsafe void OnAddonTransitionEvent(AddonEvent type, AddonArgs args)
	{
		if (isTeleporting && Configuration.EnableAddonSuppression && TransitionAddonNames.Contains(args.AddonName))
		{
			AtkUnitBase* address = (AtkUnitBase*)args.Addon.Address;
			if (address != null)
			{
				address->IsVisible = false;
				address->Alpha = 0;
			}
		}
	}

	private void OnCommand(string command, string args)
	{
		string[] array = args.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (array.Length == 0)
		{
			ToggleConfigUi();
			return;
		}
		string text = array[0];
		if (!(text == "status"))
		{
			if (text == "debug")
			{
				Configuration.DebugLogging = !Configuration.DebugLogging;
				Configuration.Save();
				Log.Information("Debug logging: {Enabled}", Configuration.DebugLogging);
			}
			else
			{
				ToggleConfigUi();
			}
		}
		else
		{
			Log.Information("Cast={Cast} Transition={Transition} FastFade={FastFade} AddonSuppression={Addon}", Configuration.EnableInstantCast, Configuration.EnableTransitionSkip, Configuration.EnableFastFade, Configuration.EnableAddonSuppression);
		}
	}

	public void ToggleConfigUi()
	{
		ConfigWindow.Toggle();
	}

	public void Dispose()
	{
		PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
		PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
		Framework.Update -= OnFrameworkUpdate;
		UnregisterAddonListeners();
		CommandManager.RemoveHandler(CommandName);
		WindowSystem.RemoveAllWindows();
		ConfigWindow.Dispose();
		getAdjustedCastTimeHook?.Disable();
		getAdjustedCastTimeHook?.Dispose();
		setOpenTransitionHook?.Disable();
		setOpenTransitionHook?.Dispose();
	}

	private void RegisterAddonListeners()
	{
		foreach (string addonName in TransitionAddonNames)
		{
			AddonLifecycle.RegisterListener((AddonEvent)14, addonName, OnAddonTransitionEvent);
			AddonLifecycle.RegisterListener((AddonEvent)18, addonName, OnAddonTransitionEvent);
			AddonLifecycle.RegisterListener((AddonEvent)8, addonName, OnAddonTransitionEvent);
		}
	}

	private void UnregisterAddonListeners()
	{
		foreach (string addonName in TransitionAddonNames)
		{
			AddonLifecycle.UnregisterListener((AddonEvent)14, addonName, OnAddonTransitionEvent);
			AddonLifecycle.UnregisterListener((AddonEvent)18, addonName, OnAddonTransitionEvent);
			AddonLifecycle.UnregisterListener((AddonEvent)8, addonName, OnAddonTransitionEvent);
		}
	}
}
