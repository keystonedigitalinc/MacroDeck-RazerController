using System.Net.Http.Json;
using System.Text.Json;
using Serilog;

namespace KeystoneDigital.RazerController.Chroma;

internal enum ChromaOutcome
{
	Applied,

	/// <summary>No Chroma SDK server answered: the Chroma module is not installed, or Chroma apps are off.</summary>
	Unavailable,

	/// <summary>The server answered but refused the effect, for example because this app is not allowed.</summary>
	Rejected,
}

/// <summary>
/// Lighting through the Razer Chroma SDK's local REST server, so it works without Synapse control. A Chroma
/// effect is an overlay that lasts only while this app holds a session: the session needs a heartbeat, and
/// ending it hands lighting back to the Synapse profile. What each device shows is kept, so animations can
/// draw their next frame and a lost session (for example after Synapse restarts) can be put back.
/// </summary>
internal sealed class ChromaClient : IDisposable
{
	/// <summary>
	/// Local only. The SDK's HTTPS endpoint is reached through the public name chromasdk.io, whose DNS lookup
	/// leaves the PC, so it is not used.
	/// </summary>
	public static IReadOnlyList<Uri> DefaultEndpoints { get; } =
	[
		new("http://localhost:54235/razer/chromasdk"),
	];

	private readonly HttpClient _http;
	private readonly IReadOnlyList<Uri> _endpoints;
	private readonly TimeSpan _settle;
	private readonly TimeProvider _clock;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, (ChromaLighting Lighting, DateTimeOffset Since)> _shown = new(StringComparer.Ordinal);

	/// <summary>Device types the server refused, so animation frames are not sent to them again and again.</summary>
	private readonly HashSet<string> _refused = new(StringComparer.Ordinal);

	private Uri? _session;

	public ChromaClient(HttpMessageHandler handler, ILogger logger, IReadOnlyList<Uri>? endpoints = null, TimeSpan? settle = null, TimeProvider? clock = null)
	{
		_http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
		_endpoints = endpoints ?? DefaultEndpoints;
		_settle = settle ?? TimeSpan.FromSeconds(1);
		_clock = clock ?? TimeProvider.System;
		_logger = logger.ForContext<ChromaClient>();
	}

	public bool HasSession => Volatile.Read(ref _session) is not null;

	public bool IsAnimating
	{
		get
		{
			lock (_shown) return _shown.Values.Any(entry => entry.Lighting.Animated);
		}
	}

	/// <summary>The lighting the plugin last drew, as a lowercase effect name, or "synapse" while Synapse has the lighting.</summary>
	public string Effect
	{
		get
		{
			lock (_shown)
			{
				return _shown.Count == 0
					? "synapse"
					: _shown.Values.MaxBy(entry => entry.Since).Lighting.Mode.ToString().ToLowerInvariant();
			}
		}
	}

	public async Task<ChromaOutcome> ApplyAsync(IReadOnlyList<ChromaDevice> devices, ChromaLighting lighting, CancellationToken ct)
	{
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			var now = _clock.GetUtcNow();
			foreach (var device in devices) _refused.Remove(device.Name);

			// One retry with a new session covers a server that restarted since the last press.
			for (var attempt = 0; attempt < 2; attempt++)
			{
				if (_session is null && !await OpenAsync(ct).ConfigureAwait(false)) return ChromaOutcome.Unavailable;

				var accepted = await SendAsync(devices.Select(device => (device, lighting, TimeSpan.Zero)), ct).ConfigureAwait(false);
				if (accepted is null)
				{
					_session = null;
					continue;
				}

				lock (_shown)
				{
					foreach (var device in devices)
					{
						if (accepted.Contains(device)) _shown[device.Name] = (lighting, now);
						else _shown.Remove(device.Name);
					}
				}

				// "All devices" includes types this PC does not have, which the server refuses one by one.
				return accepted.Count > 0 ? ChromaOutcome.Applied : ChromaOutcome.Rejected;
			}

			return ChromaOutcome.Unavailable;
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Draws the next frame of every animated device.</summary>
	public async Task AnimateAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (_session is null) return;

			var frames = Frames(onlyAnimated: true);
			if (frames.Count > 0 && await SendAsync(frames, ct).ConfigureAwait(false) is null)
			{
				_session = null;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Ends the session, which gives every device's lighting back to Synapse.</summary>
	public async Task ReleaseAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			lock (_shown) _shown.Clear();
			if (_session is not { } session) return;

			_session = null;
			try
			{
				using var _ = await _http.DeleteAsync(session, ct).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				// The server is gone, so the session is too.
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>Keeps the session alive; when it was lost, opens a new one and puts every device's lighting back.</summary>
	public async Task HeartbeatAsync(CancellationToken ct)
	{
		await _gate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (_session is { } session)
			{
				try
				{
					using var response = await _http.PutAsync(new Uri(session + "/heartbeat"), null, ct).ConfigureAwait(false);
					if (response.IsSuccessStatusCode) return;
				}
				catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
				{
				}

				_logger.Information("Chroma session lost");
				_session = null;
			}

			var frames = Frames(onlyAnimated: false);
			if (frames.Count == 0 || !await OpenAsync(ct).ConfigureAwait(false)) return;

			if (await SendAsync(frames, ct).ConfigureAwait(false) is null) _session = null;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		_http.Dispose();
		_gate.Dispose();
	}

	private List<(ChromaDevice Device, ChromaLighting Lighting, TimeSpan Elapsed)> Frames(bool onlyAnimated)
	{
		var now = _clock.GetUtcNow();
		lock (_shown)
		{
			return [.. _shown
				.Where(pair => (!onlyAnimated || pair.Value.Lighting.Animated) && !_refused.Contains(pair.Key))
				.Select(pair => (ChromaDevice.Find(pair.Key)!, pair.Value.Lighting, now - pair.Value.Since))];
		}
	}

	private async Task<bool> OpenAsync(CancellationToken ct)
	{
		var app = new
		{
			title = "Macro Deck - Razer App Controller",
			description = "Lighting buttons from the Razer App Controller plugin for Macro Deck",
			author = new { name = "Keystone Digital", contact = "https://github.com/keystonedigitalinc/MacroDeck-RazerController" },
			device_supported = ChromaDevice.All.Select(device => device.Name).ToArray(),
			category = "application",
		};

		foreach (var endpoint in _endpoints)
		{
			try
			{
				using var response = await _http.PostAsJsonAsync(endpoint, app, ct).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode) continue;

				using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
				if (!body.RootElement.TryGetProperty("uri", out var uri) || uri.GetString() is not { } text) continue;

				_session = new Uri(text.TrimEnd('/'));
				_logger.Information("Chroma session opened at {Endpoint}", endpoint);

				// The server needs a moment after a new session before it shows effects.
				if (_settle > TimeSpan.Zero) await Task.Delay(_settle, ct).ConfigureAwait(false);
				return true;
			}
			catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
				|| (ex is TaskCanceledException && !ct.IsCancellationRequested))
			{
				_logger.Debug(ex, "No Chroma SDK server at {Endpoint}", endpoint);
			}
		}

		return false;
	}

	/// <summary>The devices that took their frame, or null when the session no longer answers.</summary>
	private async Task<List<ChromaDevice>?> SendAsync(IEnumerable<(ChromaDevice Device, ChromaLighting Lighting, TimeSpan Elapsed)> frames, CancellationToken ct)
	{
		var accepted = new List<ChromaDevice>();

		foreach (var (device, lighting, elapsed) in frames)
		{
			try
			{
				using var response = await _http.PutAsJsonAsync(new Uri(_session + "/" + device.Name), lighting.Frame(device, elapsed), ct).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode) return null;

				using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
				if (body.RootElement.TryGetProperty("result", out var result) && result.TryGetInt64(out var code) && code != 0)
				{
					if (_refused.Add(device.Name)) _logger.Information("Chroma refused {Mode} on {Device}: result {Code}", lighting.Mode, device.Name, code);
					continue;
				}

				accepted.Add(device);
			}
			catch (Exception ex) when (ex is HttpRequestException or JsonException
				|| (ex is TaskCanceledException && !ct.IsCancellationRequested))
			{
				return null;
			}
		}

		return accepted;
	}
}
