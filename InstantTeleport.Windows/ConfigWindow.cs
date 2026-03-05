using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace InstantTeleport.Windows;

public class ConfigWindow : Window, IDisposable
{
	private readonly Plugin plugin;

	private readonly Configuration configuration;

	public ConfigWindow(Plugin plugin)
		: base("Instant Teleport")
	{
		this.plugin = plugin;
		configuration = plugin.Configuration;
		((Window)this).Size = new Vector2(460f, 260f);
		((Window)this).SizeCondition = (ImGuiCond)4;
	}

	public void Dispose()
	{
	}

	public override void Draw()
	{
		ImGui.Text("FFXIV Instant Teleport");
		ImGui.Separator();
		bool enableInstantCast = configuration.EnableInstantCast;
		if (ImGui.Checkbox("Instant Cast (Teleport/Return)", ref enableInstantCast))
		{
			configuration.EnableInstantCast = enableInstantCast;
			configuration.Save();
		}
		bool enableTransitionSkip = configuration.EnableTransitionSkip;
		if (ImGui.Checkbox("Skip Transition Screen", ref enableTransitionSkip))
		{
			configuration.EnableTransitionSkip = enableTransitionSkip;
			configuration.Save();
		}
		bool enableFastFade = configuration.EnableFastFade;
		if (ImGui.Checkbox("Force Fast Fade", ref enableFastFade))
		{
			configuration.EnableFastFade = enableFastFade;
			configuration.Save();
		}
		bool enableAddonSuppression = configuration.EnableAddonSuppression;
		if (ImGui.Checkbox("Suppress Transition Addons", ref enableAddonSuppression))
		{
			configuration.EnableAddonSuppression = enableAddonSuppression;
			configuration.Save();
		}
		float fadeDurationOverride = configuration.FadeDurationOverride;
		if (ImGui.SliderFloat("Fade Override (seconds)", ref fadeDurationOverride, 0f, 0.25f, "%.3f", ImGuiSliderFlags.None))
		{
			configuration.FadeDurationOverride = fadeDurationOverride;
			configuration.Save();
		}
		bool debugLogging = configuration.DebugLogging;
		if (ImGui.Checkbox("Debug Logging", ref debugLogging))
		{
			configuration.DebugLogging = debugLogging;
			configuration.Save();
		}
		ImGui.Separator();
		ImGui.TextDisabled("Command: /instanttp");
		if (ImGui.Button("Close", default(Vector2)))
		{
			plugin.ToggleConfigUi();
		}
	}
}
