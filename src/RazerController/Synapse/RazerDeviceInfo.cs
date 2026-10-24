using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

internal enum DeviceCategory
{
	Mouse,
	Keyboard,
	Other,
}

internal sealed record SynapseProfile(string Guid, string Name);

/// <summary>The active quick effect. <see cref="Setting"/> keeps every field Synapse stored for it (colours, duration, direction).</summary>
internal sealed record LightingEffect(int Id, string? Color, JsonElement? Setting = null)
{
	public string? Read(string field) =>
		Setting is { ValueKind: JsonValueKind.Object } setting && setting.TryGetProperty(field, out var value) ? value.ToString() : null;
}

/// <summary>The active profile's gaming mode. <see cref="Raw"/> keeps every field so a write changes only what it means to.</summary>
internal sealed record GamingModeState(bool On, JsonElement Raw);

/// <summary>
/// The active profile's DPI stages as Synapse stores them: five slots, of which <see cref="Visible"/> are in use.
/// <see cref="Active"/> is the 1-based slot number. <see cref="Raw"/> is the slots array, sent back unchanged.
/// </summary>
internal sealed record DpiStageSet(int Active, bool Enable, IReadOnlyList<int> Visible, JsonElement Raw)
{
	/// <summary>The 1-based slot of the <paramref name="number"/>th stage in use, or null.</summary>
	public int? SlotOf(int number) => number >= 1 && number <= Visible.Count ? Visible[number - 1] : null;

	/// <summary>The X DPI stored in a 1-based slot, or null.</summary>
	public int? DpiOf(int slot) =>
		Raw.ValueKind == JsonValueKind.Array && slot >= 1 && slot <= Raw.GetArrayLength()
		&& Raw[slot - 1].ValueKind == JsonValueKind.Object && Raw[slot - 1].TryGetProperty("x", out var x) && x.TryGetInt32(out var dpi)
			? dpi
			: null;

	/// <summary>The active stage's number among the stages in use, or null.</summary>
	public int? ActiveNumber => Visible.ToList().IndexOf(Active) is var index and >= 0 ? index + 1 : null;

	/// <summary>The highest DPI in any slot; Synapse fills unused slots with the mouse's maximum.</summary>
	public int MaxDpi => Enumerable.Range(1, Raw.ValueKind == JsonValueKind.Array ? Raw.GetArrayLength() : 0).Select(DpiOf).Max() ?? 0;

	/// <summary>The slot one stage up or down from the active one, or null at either end.</summary>
	public int? Step(bool up)
	{
		var position = Visible.ToList().IndexOf(Active);
		if (position < 0) return Visible.Count == 0 ? null : (up ? Visible[0] : Visible[^1]);

		var target = up ? position + 1 : position - 1;
		return target >= 0 && target < Visible.Count ? Visible[target] : null;
	}
}

internal sealed record RazerDeviceInfo(
	string Serial,
	int ProductId,
	string Name,
	DeviceCategory Category,
	string Channel,
	IReadOnlyList<SynapseProfile> Profiles,
	string? ActiveProfileGuid,
	LightingEffect? Effect,
	GamingModeState? GamingMode,
	int? Brightness = null,
	DpiStageSet? DpiStages = null,
	int? BrightnessLevel = null)
{
	public string? ActiveProfileName => Profiles.FirstOrDefault(p => p.Guid == ActiveProfileGuid)?.Name;
}

internal sealed record CatalogParseResult(IReadOnlyList<RazerDeviceInfo> Devices, string? SynapseVersion, int WindowCount)
{
	public static CatalogParseResult Empty { get; } = new([], null, 0);
}
