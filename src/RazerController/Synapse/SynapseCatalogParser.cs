using System.Globalization;
using System.Text.Json;

namespace KeystoneDigital.RazerController.Synapse;

/// <summary>
/// Turns the <see cref="InspectorScripts.Catalog"/> snapshot into devices. Synapse's storage is not a
/// contract, so every read is defensive: a field that is missing or the wrong shape drops that device
/// or that detail, never the whole catalog.
/// </summary>
internal static class SynapseCatalogParser
{
	public static CatalogParseResult Parse(JsonElement? snapshot)
	{
		if (snapshot is not { ValueKind: JsonValueKind.Object } root) return CatalogParseResult.Empty;

		var version = ReadString(root, "version");
		if (!root.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array)
		{
			return new CatalogParseResult([], version, 0);
		}

		root.TryGetProperty("storage", out var storage);
		var connected = ParseEmbedded(ReadString(storage, "connectedDeviceInfo"));
		var documents = storage.ValueKind == JsonValueKind.Object && storage.TryGetProperty("devices", out var found) ? found : default;

		var devices = new List<RazerDeviceInfo>();

		foreach (var window in windows.EnumerateArray())
		{
			if (ReadInt(window, "productId") is not { } productId) continue;

			var channel = ReadString(window, "title");
			if (string.IsNullOrEmpty(channel)) continue;

			var document = ParseEmbedded(ReadString(documents, productId.ToString(CultureInfo.InvariantCulture)));
			if (document is not { ValueKind: JsonValueKind.Object } doc) continue;

			var device = BuildDevice(productId, channel, doc, FindConnectedEntry(connected, productId));
			if (device is not null) devices.Add(device);
		}

		return new CatalogParseResult(devices, version, windows.GetArrayLength());
	}

	private static RazerDeviceInfo? BuildDevice(int productId, string channel, JsonElement doc, JsonElement? connected)
	{
		var serial = connected is { } info ? ReadString(info, "serialNumber") : null;
		var name = connected is { } info2 && info2.TryGetProperty("name", out var names) ? ReadString(names, "en") : null;
		var category = connected is { } info3 ? ReadString(info3, "category") : null;

		if (doc.TryGetProperty("deviceMetadatas", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
		{
			var first = metadata.EnumerateObject().FirstOrDefault();
			if (first.Value.ValueKind == JsonValueKind.Object)
			{
				serial ??= first.Name;
				name ??= ReadString(first.Value, "name");
			}
		}

		category ??= ReadString(doc, "category");
		if (string.IsNullOrEmpty(serial)) return null;

		var profiles = new List<SynapseProfile>();
		JsonElement? activeProfile = null;
		var activeGuid = ReadString(doc, "activeProfile");

		if (doc.TryGetProperty("profiles", out var profileArray) && profileArray.ValueKind == JsonValueKind.Array)
		{
			foreach (var profile in profileArray.EnumerateArray())
			{
				var guid = ReadString(profile, "guid");
				if (string.IsNullOrEmpty(guid)) continue;

				profiles.Add(new SynapseProfile(guid, ReadString(profile, "name") ?? guid));
				if (guid == activeGuid) activeProfile = profile;
			}
		}

		return new RazerDeviceInfo(
			serial,
			productId,
			name ?? $"Razer device {productId}",
			category switch
			{
				"MOUSE" => DeviceCategory.Mouse,
				"KEYBOARD" => DeviceCategory.Keyboard,
				_ => DeviceCategory.Other,
			},
			channel,
			profiles,
			activeGuid,
			activeProfile is { } active ? ReadEffect(active) : null,
			activeProfile is { } active2 ? ReadGamingMode(active2) : null,
			activeProfile is { } active3 ? ReadBrightness(active3) : null,
			activeProfile is { } active4 ? ReadDpiStages(active4) : null);
	}

	private static LightingEffect? ReadEffect(JsonElement profile)
	{
		if (!profile.TryGetProperty("quickEffects", out var effects) || effects.ValueKind != JsonValueKind.Object) return null;
		if (ReadInt(effects, "selectedEffectId") is not { } effectId) return null;

		if (!effects.TryGetProperty("selectedEffectSetting", out var setting) || setting.ValueKind != JsonValueKind.Object) return new LightingEffect(effectId, null);

		return new LightingEffect(effectId, ReadString(setting, "color1"), setting.Clone());
	}

	/// <summary>The active profile's brightness, 0 when Synapse has it switched off.</summary>
	private static int? ReadBrightness(JsonElement profile)
	{
		if (!profile.TryGetProperty("brightness", out var brightness) || brightness.ValueKind != JsonValueKind.Object) return null;
		if (brightness.TryGetProperty("isEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return 0;

		return ReadInt(brightness, "value");
	}

	/// <summary>The active profile's DPI stage slots. Only mice have them.</summary>
	private static DpiStageSet? ReadDpiStages(JsonElement profile)
	{
		if (!profile.TryGetProperty("dpiStages", out var stages) || stages.ValueKind != JsonValueKind.Object) return null;
		if (!stages.TryGetProperty("stages", out var slots) || slots.ValueKind != JsonValueKind.Array) return null;

		var visible = new List<int>();
		var slot = 0;
		foreach (var stage in slots.EnumerateArray())
		{
			slot++;
			if (stage.ValueKind == JsonValueKind.Object && stage.TryGetProperty("visible", out var shown) && shown.ValueKind == JsonValueKind.True) visible.Add(slot);
		}

		var enable = !stages.TryGetProperty("enable", out var on) || on.ValueKind != JsonValueKind.False;
		return new DpiStageSet(ReadInt(stages, "active") ?? 0, enable, visible, slots.Clone());
	}

	private static GamingModeState? ReadGamingMode(JsonElement profile)
	{
		if (!profile.TryGetProperty("gamingMode", out var mode) || mode.ValueKind != JsonValueKind.Object) return null;

		var on = ReadInt(mode, "state") > 0;
		return new GamingModeState(on, mode.Clone());
	}

	private static JsonElement? FindConnectedEntry(JsonElement? connected, int productId)
	{
		if (connected is not { ValueKind: JsonValueKind.Array } list) return null;

		foreach (var entry in list.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.Object
				&& ReadInt(entry, "productId") == productId)
			{
				return entry;
			}
		}

		return null;
	}

	private static JsonElement? ParseEmbedded(string? json)
	{
		if (string.IsNullOrEmpty(json)) return null;

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.Clone();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	// JsonElement.TryGetInt32 throws on a non-number, so the kind is checked first.
	private static int? ReadInt(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(property, out var value)
		&& value.ValueKind == JsonValueKind.Number
		&& value.TryGetInt32(out var number)
			? number
			: null;

	private static string? ReadString(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object
		&& element.TryGetProperty(property, out var value)
		&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
