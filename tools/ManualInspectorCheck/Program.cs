using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeystoneDigital.RazerController.Synapse;

[assembly: SupportedOSPlatform("windows")]

// Manual checks against the real Synapse on this machine. Never run by the test suite.
//   --dump <path>   write the catalog snapshot with serial numbers masked, for test fixtures
if (args is ["--dump", var path])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	await inspector.ConnectAsync(CancellationToken.None);

	var snapshot = await inspector.EvaluateAsync(InspectorScripts.Catalog, CancellationToken.None);
	var json = JsonSerializer.Serialize(TrimToParsedFields(snapshot), new JsonSerializerOptions { WriteIndented = true });

	var serials = new Dictionary<string, string>();
	json = Regex.Replace(json, @"(PM\d{4}F\d{8,}|\d{6}H\d{8})", m =>
	{
		if (!serials.TryGetValue(m.Value, out var masked))
		{
			masked = $"SERIAL-{serials.Count + 1}";
			serials[m.Value] = masked;
		}

		return masked;
	});

	await File.WriteAllTextAsync(path, json);
	Console.WriteLine($"Wrote {path}: {json.Length} chars, {serials.Count} serial(s) masked.");
	return 0;
}

//   --eval <file> <out>  evaluate a script from a file in Synapse's main process and write the result as JSON.
//                        For read-only exploration; the script decides what it touches.
if (args is ["--eval", var scriptPath, var outPath])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	await inspector.ConnectAsync(CancellationToken.None);

	var result = await inspector.EvaluateAsync(await File.ReadAllTextAsync(scriptPath), CancellationToken.None);
	var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
	await File.WriteAllTextAsync(outPath, json);
	Console.WriteLine($"Wrote {outPath}: {json.Length} chars.");
	return 0;
}

//   --dpi   read the mouse's current DPI and stage over HID. Read only.
if (args is ["--dpi"])
{
	using var dpi = KeystoneDigital.RazerController.Hid.MouseDpiService.CreateDefault(Serilog.Core.Logger.None);
	await dpi.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"mouse dpi: {dpi.State?.ToString() ?? "no mouse answered"}");
	return 0;
}

//   --lighting-live   keyboard brightness 50 and back, keyboard two-colour breathing and back, mouse wave up/down
//                     and back. Changes the active profiles while it runs; restores each step.
if (args is ["--lighting-live"])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	var synapse = new SynapseService(inspector, Serilog.Core.Logger.None);
	await synapse.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"status={synapse.Status}");

	async Task Step(string label, string serial, string type, object payload, Func<RazerDeviceInfo, bool> applied)
	{
		Console.WriteLine($"{DateTime.Now:HH:mm:ss} {label}: {await synapse.SendAndConfirmAsync(serial, type, payload, applied, CancellationToken.None)}");
		await Task.Delay(TimeSpan.FromSeconds(5));
	}

	if (synapse.FirstOf(DeviceCategory.Keyboard) is { Effect: { } original } keyboard)
	{
		var brightness = keyboard.Brightness ?? 100;
		Console.WriteLine($"keyboard brightness={brightness} effect={original.Id} setting={original.Setting?.GetRawText()}");
		await Step("keyboard brightness -> 50", keyboard.Serial, "ON_SET_BRIGHTNESS", new { brightness = new { isEnabled = true, value = 50 } }, d => d.Brightness == 50);
		await Step("keyboard brightness -> back", keyboard.Serial, "ON_SET_BRIGHTNESS", new { brightness = new { isEnabled = brightness > 0, value = brightness } }, d => d.Brightness == brightness);

		await Step("keyboard breathing red/blue", keyboard.Serial, "ON_SET_EFFECTS",
			new { effectSetting = new { selectedEffectId = 2, selectedEffectSetting = new { color1 = "#ff0000", color2 = "#0000ff", isRandom = false } } },
			d => d.Effect?.Id == 2 && d.Effect.Read("color2") == "#0000ff");
		await Step("keyboard effect -> back", keyboard.Serial, "ON_SET_EFFECTS",
			original.Setting is { } setting
				? new { effectSetting = new { selectedEffectId = original.Id, selectedEffectSetting = setting } }
				: new { effectSetting = new { selectedEffectId = original.Id } },
			d => d.Effect?.Id == original.Id);
	}

	if (synapse.FirstOf(DeviceCategory.Mouse) is { Effect: { } mouseOriginal } mouse)
	{
		Console.WriteLine($"mouse effect={mouseOriginal.Id} setting={mouseOriginal.Setting?.GetRawText()}");
		await Step("mouse wave up", mouse.Serial, "ON_SET_EFFECTS", new { effectSetting = new { selectedEffectId = 4, selectedEffectSetting = new { direction = 3 } } }, d => d.Effect?.Id == 4 && d.Effect.Read("direction") == "3");
		await Step("mouse wave down", mouse.Serial, "ON_SET_EFFECTS", new { effectSetting = new { selectedEffectId = 4, selectedEffectSetting = new { direction = 4 } } }, d => d.Effect?.Id == 4 && d.Effect.Read("direction") == "4");
		await Step("mouse effect -> back", mouse.Serial, "ON_SET_EFFECTS",
			mouseOriginal.Setting is { } mouseSetting
				? new { effectSetting = new { selectedEffectId = mouseOriginal.Id, selectedEffectSetting = mouseSetting } }
				: new { effectSetting = new { selectedEffectId = mouseOriginal.Id } },
			d => d.Effect?.Id == mouseOriginal.Id);
	}

	return 0;
}

//   --roundtrip     through SynapseService: switch the mouse profile, the keyboard effect and gaming
//                   mode, restoring each; then read (never write) the mouse DPI over HID
if (args is ["--roundtrip"])
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	var synapse = new SynapseService(inspector, Serilog.Core.Logger.None);
	await synapse.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"status={synapse.Status} synapse={synapse.SynapseVersion} devices={string.Join(", ", synapse.Devices.Select(d => $"{d.Name} [{d.Category}, {d.Profiles.Count} profiles, active '{d.ActiveProfileName}']"))}");

	async Task Step(string label, string serial, string type, object payload, Func<RazerDeviceInfo, bool> applied) =>
		Console.WriteLine($"{label}: {await synapse.SendAndConfirmAsync(serial, type, payload, applied, CancellationToken.None)}");

	if (synapse.FirstOf(DeviceCategory.Mouse) is { Profiles.Count: > 1 } mouse)
	{
		var original = mouse.ActiveProfileGuid!;
		var other = mouse.Profiles.First(p => p.Guid != original).Guid;
		await Step("mouse profile -> other", mouse.Serial, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = other }, d => d.ActiveProfileGuid == other);
		await Step("mouse profile -> back", mouse.Serial, "ON_SWITCH_PROFILE", new { newActiveProfileGUID = original }, d => d.ActiveProfileGuid == original);
	}

	if (synapse.FirstOf(DeviceCategory.Keyboard) is { Effect: { } effect, GamingMode: { } gaming } keyboard)
	{
		await Step("keyboard effect -> spectrum", keyboard.Serial, "ON_SET_EFFECTS", new { effectSetting = new { selectedEffectId = 3 } }, d => d.Effect?.Id == 3);
		object restore = effect.Color is null
			? new { effectSetting = new { selectedEffectId = effect.Id } }
			: new { effectSetting = new { selectedEffectId = effect.Id, selectedEffectSetting = new { color1 = effect.Color } } };
		await Step("keyboard effect -> back", keyboard.Serial, "ON_SET_EFFECTS", restore, d => d.Effect?.Id == effect.Id);

		await Step("gaming mode -> on", keyboard.Serial, "ON_SET_GAME_MODE", new { gamingMode = KeystoneDigital.RazerController.Actions.GamingModeAction.WithState(gaming, !gaming.On) }, d => d.GamingMode?.On == !gaming.On);
		await Step("gaming mode -> back", keyboard.Serial, "ON_SET_GAME_MODE", new { gamingMode = KeystoneDigital.RazerController.Actions.GamingModeAction.WithState(gaming, gaming.On) }, d => d.GamingMode?.On == gaming.On);
	}

	using var dpi = KeystoneDigital.RazerController.Hid.MouseDpiService.CreateDefault(Serilog.Core.Logger.None);
	await dpi.RefreshAsync(CancellationToken.None);
	Console.WriteLine($"mouse dpi (read only): {dpi.State?.ToString() ?? "no mouse answered"}");
	return 0;
}

//   --soak <seconds> keep one inspector session, read the catalog every 2 s, print every drop
if (args is ["--soak", var secondsText] && int.TryParse(secondsText, out var seconds))
{
	using var fetcher = new HttpCdpTargetFetcher();
	using var inspector = new CdpInspector(fetcher, new WebSocketRawDuplexFactory());
	var watch = System.Diagnostics.Stopwatch.StartNew();
	var drops = 0;
	while (watch.Elapsed.TotalSeconds < seconds)
	{
		try
		{
			if (!inspector.IsConnected) await inspector.ConnectAsync(CancellationToken.None);
			await inspector.EvaluateAsync(InspectorScripts.Catalog, CancellationToken.None);
		}
		catch (SynapseInspectorException ex)
		{
			drops++;
			Console.WriteLine($"{watch.Elapsed.TotalSeconds,6:F1}s drop: {ex.Code} {ex.Message} {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
		}
		await Task.Delay(2000);
	}
	Console.WriteLine($"{drops} drop(s) in {seconds}s");
	return 0;
}

//   --synapse-state  read-only: is Synapse running, and with the inspector flag?
if (args is ["--synapse-state"])
{
	Console.WriteLine(new KeystoneDigital.RazerController.Autostart.WindowsSynapseProcess().GetState());
	return 0;
}

Console.WriteLine("Usage: ManualInspectorCheck --dump <path> | --roundtrip | --soak <seconds> | --synapse-state");
return 2;

// The fixture is committed to a public repository, so it keeps only what SynapseCatalogParser reads:
// keybinds, macros, calibration and the rest of each profile stay out.
static System.Text.Json.Nodes.JsonNode? TrimToParsedFields(JsonElement? snapshot)
{
	var root = snapshot is { } value ? System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()) : null;
	if (root?["storage"]?["devices"] is not System.Text.Json.Nodes.JsonObject devices) return root;

	foreach (var (pid, raw) in devices.ToList())
	{
		if (raw is null) continue;
		var doc = System.Text.Json.Nodes.JsonNode.Parse(raw.GetValue<string>())!.AsObject();
		var profiles = new System.Text.Json.Nodes.JsonArray();
		foreach (var profile in doc["profiles"]?.AsArray() ?? [])
		{
			var kept = new System.Text.Json.Nodes.JsonObject();
			foreach (var key in new[] { "guid", "name", "quickEffects", "gamingMode" })
			{
				if (profile?[key] is { } field) kept[key] = field.DeepClone();
			}

			profiles.Add(kept);
		}

		var trimmed = new System.Text.Json.Nodes.JsonObject
		{
			["productId"] = doc["productId"]?.DeepClone(),
			["category"] = doc["category"]?.DeepClone(),
			["activeProfile"] = doc["activeProfile"]?.DeepClone(),
			["profiles"] = profiles,
			["deviceMetadatas"] = doc["deviceMetadatas"]?.DeepClone(),
		};
		devices[pid] = trimmed.ToJsonString();
	}

	return root;
}
