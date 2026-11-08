using System;
using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>
	/// What whoever started the Player says about the run, read when telemetry starts: the folder and the resource on its
	/// command line, and the resource in the environment variables of OpenTelemetry. A tool that runs builds side by side,
	/// such as the workspace of Autopilot, gives each its own folder and names the build, the commit and the seed this
	/// way, without the game knowing.
	/// </summary>
	internal static class TelemetryLaunch
	{
		internal const string DirectoryArgument = "-dexc-telemetry-directory";
		internal const string ResourceArgument = "-dexc-telemetry-resource";
		internal const string ResourceVariable = "OTEL_RESOURCE_ATTRIBUTES";
		internal const string ServiceNameVariable = "OTEL_SERVICE_NAME";

		/// <summary>The folder after <c>-dexc-telemetry-directory</c>, the last one given, or null.</summary>
		internal static string Directory(IReadOnlyList<string> arguments)
		{
			string directory = null;
			for (var i = 0; i + 1 < arguments.Count; i++)
			{
				if (arguments[i] == DirectoryArgument)
				{
					directory = arguments[++i];
				}
			}
			return directory;
		}

		/// <summary>Each <c>-dexc-telemetry-resource key=value</c>, in order. One without a key is left out.</summary>
		internal static List<KeyValuePair<string, string>> CommandLineResource(IReadOnlyList<string> arguments)
		{
			var resource = new List<KeyValuePair<string, string>>();
			for (var i = 0; i + 1 < arguments.Count; i++)
			{
				if (arguments[i] != ResourceArgument)
				{
					continue;
				}
				var pair = arguments[++i];
				var equals = pair.IndexOf('=');
				if (equals > 0)
				{
					resource.Add(new KeyValuePair<string, string>(pair.Substring(0, equals).Trim(), pair.Substring(equals + 1)));
				}
			}
			return resource;
		}

		/// <summary>
		/// The resource of <c>OTEL_RESOURCE_ATTRIBUTES</c> and <c>OTEL_SERVICE_NAME</c>, as the SDK specification reads them:
		/// <c>key=value</c> pairs between commas, the values percent-encoded. A value that cannot be read is discarded whole,
		/// and the service name wins over a <c>service.name</c> among the attributes.
		/// </summary>
		internal static List<KeyValuePair<string, string>> EnvironmentResource(string attributes, string serviceName)
		{
			var resource = new List<KeyValuePair<string, string>>();
			if (!string.IsNullOrWhiteSpace(attributes))
			{
				try
				{
					foreach (var item in attributes.Split(','))
					{
						if (item.Trim().Length == 0)
						{
							continue;
						}
						var equals = item.IndexOf('=');
						var key = equals > 0 ? item.Substring(0, equals).Trim() : string.Empty;
						if (key.Length == 0)
						{
							throw new FormatException("Not key=value: " + item);
						}
						resource.Add(new KeyValuePair<string, string>(key, Uri.UnescapeDataString(item.Substring(equals + 1).Trim())));
					}
				}
				catch (Exception exception) when (exception is FormatException || exception is UriFormatException)
				{
					UnityEngine.Debug.LogWarning("Dexc Telemetry: " + ResourceVariable + " is left out: " + exception.Message);
					resource.Clear();
				}
			}
			if (!string.IsNullOrWhiteSpace(serviceName))
			{
				resource.Add(new KeyValuePair<string, string>("service.name", serviceName.Trim()));
			}
			return resource;
		}

		/// <summary>The layers in order, a later one winning for the same key. A key keeps the place it first had.</summary>
		internal static List<KeyValuePair<string, string>> Merge(params IEnumerable<KeyValuePair<string, string>>[] layers)
		{
			var order = new List<string>();
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var layer in layers)
			{
				if (layer == null)
				{
					continue;
				}
				foreach (var pair in layer)
				{
					if (!values.ContainsKey(pair.Key))
					{
						order.Add(pair.Key);
					}
					values[pair.Key] = pair.Value;
				}
			}
			var merged = new List<KeyValuePair<string, string>>(order.Count);
			foreach (var key in order)
			{
				merged.Add(new KeyValuePair<string, string>(key, values[key]));
			}
			return merged;
		}
	}
}
