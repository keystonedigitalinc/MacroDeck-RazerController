namespace KeystoneDigital.RazerController.Synapse;

/// <summary>Whether the plugin asks Synapse to show its popup after a change. On unless the user turns it off.</summary>
internal interface IPopupPreference
{
	bool Enabled { get; set; }
}

internal sealed class MemoryPopupPreference : IPopupPreference
{
	public bool Enabled { get; set; } = true;
}

/// <summary>Kept beside the Synapse control choice in the plugin's data directory, which survives updates.</summary>
internal sealed class FilePopupPreference(string directory) : IPopupPreference
{
	private const string FileName = "synapse-popups.txt";

	private readonly Lock _gate = new();

	public static FilePopupPreference FromEnvironment()
	{
		var directory = Environment.GetEnvironmentVariable("MACRO_DECK_PLUGIN_DATA_DIRECTORY");
		if (string.IsNullOrWhiteSpace(directory))
		{
			directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RazerController");
		}

		return new FilePopupPreference(directory);
	}

	public bool Enabled
	{
		get
		{
			lock (_gate)
			{
				var path = Path.Combine(directory, FileName);
				return !File.Exists(path) || File.ReadAllText(path).Trim() != "off";
			}
		}
		set
		{
			lock (_gate)
			{
				Directory.CreateDirectory(directory);
				File.WriteAllText(Path.Combine(directory, FileName), value ? "on" : "off");
			}
		}
	}
}
