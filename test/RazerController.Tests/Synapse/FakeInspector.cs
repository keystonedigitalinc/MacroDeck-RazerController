using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeystoneDigital.RazerController.Synapse;

namespace KeystoneDigital.RazerController.Tests.Synapse;

internal sealed record SentCommand(string Channel, string Type, JsonElement Payload);

/// <summary>
/// Serves the catalog fixture (mutable through helpers) and records every command script, decoding the
/// JSON target literal the script carries.
/// </summary>
internal sealed class FakeInspector : IInspector
{
	public const string MouseSerial = "SERIAL-1";
	public const string KeyboardSerial = "SERIAL-2";

	private JsonObject _snapshot;

	private FakeInspector(JsonObject snapshot) => _snapshot = snapshot;

	public static FakeInspector FromFixture() =>
		new(JsonNode.Parse(File.ReadAllText(Path.Combine(NUnit.Framework.TestContext.CurrentContext.TestDirectory, "Fixtures", "catalog-snapshot.json")))!.AsObject());

	public bool IsConnected { get; private set; }

	public Exception? ConnectError { get; set; }

	public Exception? EvaluateError { get; set; }

	public bool CommandSent { get; set; } = true;

	public Action<SentCommand>? OnCommand { get; set; }

	public List<SentCommand> Commands { get; } = [];

	/// <summary>Popup headers the plugin asked Synapse to show, in order.</summary>
	public List<string> Popups { get; } = [];

	public int ConnectCount { get; private set; }

	public Task ConnectAsync(CancellationToken cancellationToken)
	{
		ConnectCount++;
		if (ConnectError is not null) throw ConnectError;
		IsConnected = true;
		return Task.CompletedTask;
	}

	public async Task<JsonElement?> EvaluateAsync(string expression, CancellationToken cancellationToken)
	{
		// A real round trip yields; without this, concurrent presses could never overlap in tests.
		await Task.Delay(5, cancellationToken);

		if (!IsConnected) throw new SynapseInspectorException("transport_lost", "closed");

		if (EvaluateError is not null)
		{
			IsConnected = false;
			throw EvaluateError;
		}

		if (expression == InspectorScripts.Catalog)
		{
			return JsonSerializer.SerializeToElement(_snapshot);
		}

		if (expression.Contains("RzWinStorage_notification_manager", StringComparison.Ordinal))
		{
			var target = expression.Split("const target = ", 2)[1];
			var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(target));
			Popups.Add(JsonElement.ParseValue(ref reader).GetProperty("header").GetString()!);
			return JsonSerializer.SerializeToElement(new { shown = true });
		}

		var command = DecodeCommand(expression);
		Commands.Add(command);
		OnCommand?.Invoke(command);
		return JsonSerializer.SerializeToElement(new { sent = CommandSent });
	}

	public void Disconnect() => IsConnected = false;

	public void ClearWindows() => _snapshot["windows"] = new JsonArray();

	public void UnplugAll()
	{
		foreach (var pid in new[] { "103", "554" }) _snapshot["storage"]!["devices"]![pid] = null;
	}

	public void SetActiveProfile(int productId, string guid) => EditDocument(productId, doc => doc["activeProfile"] = guid);

	public void EditActiveProfile(int productId, Action<JsonObject> edit) => EditDocument(productId, doc =>
	{
		var active = doc["activeProfile"]!.GetValue<string>();
		var profile = doc["profiles"]!.AsArray().First(p => p!["guid"]!.GetValue<string>() == active)!.AsObject();
		edit(profile);
	});

	public string ProfileGuid(int productId, int index)
	{
		var doc = Document(productId);
		return doc["profiles"]![index]!["guid"]!.GetValue<string>();
	}

	public void RemoveProfile(int productId, string guid) => EditDocument(productId, doc =>
	{
		var profiles = doc["profiles"]!.AsArray();
		profiles.Remove(profiles.First(p => p!["guid"]!.GetValue<string>() == guid));
	});

	private JsonObject Document(int productId) =>
		JsonNode.Parse(_snapshot["storage"]!["devices"]![productId.ToString(System.Globalization.CultureInfo.InvariantCulture)]!.GetValue<string>())!.AsObject();

	private void EditDocument(int productId, Action<JsonObject> edit)
	{
		var doc = Document(productId);
		edit(doc);
		_snapshot["storage"]!["devices"]![productId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = doc.ToJsonString();
	}

	private static SentCommand DecodeCommand(string script)
	{
		var afterTarget = script.Split("const target = ", 2)[1];
		var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(afterTarget));
		var target = JsonElement.ParseValue(ref reader);
		return new SentCommand(target.GetProperty("channel").GetString()!, target.GetProperty("type").GetString()!, target.GetProperty("payload").Clone());
	}
}
