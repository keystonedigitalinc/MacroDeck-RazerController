using KeystoneDigital.RazerController.Settings;

namespace KeystoneDigital.RazerController.Autostart;

/// <summary>
/// Synapse rewrites its startup entry without the debug flag when a program, not Windows, starts it. After a restart
/// the user asked for, and only with Synapse control on, this puts the flag back whenever it goes missing during
/// <c>window</c>, then stops. It never runs on its own: only the Restart Synapse action and the settings screen's
/// restart start it.
/// </summary>
internal static class FlagKeeper
{
	public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

	public static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);

	/// <summary>Starts the watch without waiting for it.</summary>
	public static void Start(IAutostartRegistry registry) => _ = Task.Run(() => KeepAsync(registry, Window, Poll, CancellationToken.None));

	public static async Task KeepAsync(IAutostartRegistry registry, TimeSpan window, TimeSpan poll, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + window;

		while (DateTime.UtcNow < deadline)
		{
			try
			{
				if (registry.GetValue() is { Length: > 0 } current && !AutostartCommandLine.HasFlag(current, InspectorConfigFlow.InspectFlag))
				{
					registry.SetValue(AutostartCommandLine.AppendFlag(current, InspectorConfigFlow.InspectFlag));
				}
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
			{
				// The next poll tries again; the status variable reports a missing flag if it stays missing.
			}

			await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
		}
	}
}
