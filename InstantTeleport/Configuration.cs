using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace InstantTeleport;

[Serializable]
public class Configuration : IPluginConfiguration
{
	[NonSerialized]
	private IDalamudPluginInterface? pluginInterface;

	public int Version { get; set; } = 1;

	public bool EnableInstantCast { get; set; } = true;

	public bool EnableTransitionSkip { get; set; } = true;

	public bool EnableFastFade { get; set; } = true;

	public bool EnableAddonSuppression { get; set; } = true;

	public float FadeDurationOverride { get; set; } = 0.05f;

	public bool DebugLogging { get; set; }

	public void Initialize(IDalamudPluginInterface pluginInterface)
	{
		this.pluginInterface = pluginInterface;
	}

	public void Save()
	{
		pluginInterface?.SavePluginConfig(this);
	}
}
