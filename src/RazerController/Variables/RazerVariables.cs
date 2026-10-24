using System.Globalization;
using KeystoneDigital.RazerController.Autostart;
using KeystoneDigital.RazerController.Chroma;
using KeystoneDigital.RazerController.Hid;
using KeystoneDigital.RazerController.Synapse;
using MacroDeck.Sdk.Variables;

namespace KeystoneDigital.RazerController.Variables;

/// <summary>
/// The plugin's variables. Names are persisted in users' widgets, so they are a public API. Every read
/// comes from the services' cached state and never waits on Synapse or the mouse.
/// </summary>
internal static class RazerVariables
{
	private static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

	public static IReadOnlyList<VariableDefinition> Definitions { get; } =
	[
		VariableDefinition.Eager("razer_status", VariableType.Text, refreshInterval: Refresh) with { Id = "status" },
		VariableDefinition.Eager("razer_mouse_profile", VariableType.Text, refreshInterval: Refresh) with { Id = "mouse-profile" },
		VariableDefinition.Eager("razer_mouse_dpi", VariableType.Numeric, refreshInterval: Refresh) with { Id = "mouse-dpi" },
		VariableDefinition.Eager("razer_mouse_dpi_stage", VariableType.Text, refreshInterval: Refresh) with { Id = "mouse-dpi-stage" },
		VariableDefinition.Eager("razer_keyboard_profile", VariableType.Text, refreshInterval: Refresh) with { Id = "keyboard-profile" },
		VariableDefinition.Eager("razer_keyboard_effect", VariableType.Text, refreshInterval: Refresh) with { Id = "keyboard-effect" },
		VariableDefinition.Eager("razer_keyboard_gaming_mode", VariableType.Boolean, refreshInterval: Refresh) with { Id = "keyboard-gaming-mode" },
		VariableDefinition.Eager("razer_keyboard_brightness", VariableType.Numeric, refreshInterval: Refresh) with { Id = "keyboard-brightness" },
		VariableDefinition.Eager("razer_mouse_brightness", VariableType.Numeric, refreshInterval: Refresh) with { Id = "mouse-brightness" },
		VariableDefinition.Eager("razer_mouse_dpi_stage_number", VariableType.Numeric, refreshInterval: Refresh) with { Id = "mouse-dpi-stage-number" },
		VariableDefinition.Eager("razer_mouse_effect", VariableType.Text, refreshInterval: Refresh) with { Id = "mouse-effect" },
		VariableDefinition.Eager("razer_chroma_effect", VariableType.Text, refreshInterval: Refresh) with { Id = "chroma-effect" },
	];

	public static VariableReading Read(string localId, SynapseService synapse, MouseDpiService dpi, SetupIssue? issue = null, ChromaClient? chroma = null)
	{
		var usable = synapse.Status == SynapseStatus.Ready;
		var mouse = usable ? synapse.FirstOf(DeviceCategory.Mouse) : null;
		var keyboard = usable ? synapse.FirstOf(DeviceCategory.Keyboard) : null;
		var dpiState = dpi.State;

		object? value = localId switch
		{
			"status" => synapse.Status.ToVariableValue(issue),
			"mouse-profile" => mouse?.ActiveProfileName,
			"mouse-dpi" => dpiState?.Dpi,
			"mouse-dpi-stage" => dpiState is null ? null : string.Create(CultureInfo.InvariantCulture, $"{dpiState.StageIndex}/{dpiState.StageCount}"),
			"keyboard-profile" => keyboard?.ActiveProfileName,
			"keyboard-effect" => keyboard is { Brightness: 0 } ? "Off" : keyboard?.Effect is { } effect ? EffectName(effect.Id) : null,
			"keyboard-gaming-mode" => keyboard?.GamingMode?.On,
			"keyboard-brightness" => keyboard?.Brightness,
			"mouse-brightness" => mouse?.Brightness,
			"mouse-dpi-stage-number" => dpiState?.StageIndex,
			"mouse-effect" => mouse is { Brightness: 0 } ? "Off" : mouse?.Effect is { } mouseEffect ? EffectName(mouseEffect.Id) : null,
			"chroma-effect" => chroma?.Effect,
			_ => null,
		};

		return value is null ? VariableReading.Unavailable : VariableReading.Of(value);
	}

	// A variable value is data that widgets compare against, so it stays a stable English token rather
	// than following the UI language.
	internal static string EffectName(int id) => id switch
	{
		0 => "Off",
		1 => "Static",
		2 => "Breathing",
		3 => "Spectrum",
		4 => "Wave",
		5 => "Reactive",
		6 => "Ripple",
		7 => "Starlight",
		8 => "Fire",
		11 => "Ambient Awareness",
		12 => "Audio Meter",
		_ => id.ToString(CultureInfo.InvariantCulture),
	};
}
