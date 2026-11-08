using System.Collections.Generic;

namespace Dexc.Telemetry
{
	/// <summary>
	/// The frames worth a record of their own. A hitch, a frame over the threshold, becomes the event <c>dexc.hitch</c> with
	/// what the frame cost, what the game was doing and where the player was; each is counted in <c>dexc.hitches</c> by
	/// annotation, and the events stop at a number per export. At an interval the event <c>dexc.position</c> says where the
	/// player is, for maps of where the game goes. Both are rare, so a record may allocate. Main thread only.
	/// </summary>
	internal sealed class TelemetryFrameEvents
	{
		internal const string HitchEvent = "dexc.hitch";
		internal const string PositionEvent = "dexc.position";

		readonly TelemetryAnnotations m_Annotations;
		readonly TelemetryLogs m_Logs;
		readonly TelemetryLogger m_Hitch;
		readonly TelemetryLogger m_Position;
		readonly TelemetryCounter m_Hitches;
		readonly ITelemetryLocation m_Location;
		readonly double m_HitchMilliseconds;
		readonly int m_HitchLimit;
		readonly double m_PositionSeconds;
		TelemetryCounter m_HitchesNow;
		int m_AnnotationVersion = -1;
		int m_HitchEvents;
		double m_SincePosition;

		internal TelemetryFrameEvents(TelemetryMetrics metrics, TelemetryLogs logs, TelemetryOptions options)
		{
			m_Annotations = metrics.Annotations;
			m_Logs = logs;
			var logger = logs.Logger(TelemetryCapture.TracerName);
			m_Hitch = logger.WithEvent(HitchEvent);
			m_Position = logger.WithEvent(PositionEvent);
			m_Hitches = metrics.Counter("dexc.hitches", "{frame}");
			m_Location = options.Location;
			m_HitchMilliseconds = options.HitchMilliseconds;
			m_HitchLimit = options.HitchEventsPerExport;
			m_PositionSeconds = options.PositionSeconds;
		}

		/// <param name="mainThread">Milliseconds of the main thread, or NaN where the Player does not say.</param>
		/// <param name="allocated">Bytes allocated in the frame, or -1 where the Player does not say.</param>
		internal void OnFrame(double deltaSeconds, double milliseconds, double mainThread, long allocated, bool loading)
		{
			if (m_HitchMilliseconds > 0 && milliseconds >= m_HitchMilliseconds)
			{
				Hitch(milliseconds, mainThread, allocated, loading);
			}
			if (m_PositionSeconds > 0 && m_Location != null)
			{
				m_SincePosition += deltaSeconds;
				if (m_SincePosition >= m_PositionSeconds)
				{
					m_SincePosition = 0;
					Position();
				}
			}
		}

		/// <summary>A new export, a new allowance of hitch events.</summary>
		internal void BeforeExport()
		{
			m_HitchEvents = 0;
		}

		void Hitch(double milliseconds, double mainThread, long allocated, bool loading)
		{
			if (m_AnnotationVersion != m_Annotations.Version)
			{
				m_AnnotationVersion = m_Annotations.Version;
				m_HitchesNow = m_Annotations.Apply(m_Hitches);
			}
			m_HitchesNow.Add(1, TelemetryDetail.Basic);
			if (m_HitchEvents >= m_HitchLimit || !m_Hitch.IsEnabled(TelemetrySeverity.Info))
			{
				return;
			}
			m_HitchEvents++;
			var attributes = new List<KeyValuePair<string, TelemetryValue>>
			{
				new KeyValuePair<string, TelemetryValue>("unity.frame.time", milliseconds),
				new KeyValuePair<string, TelemetryValue>("dexc.loading", loading),
			};
			if (!double.IsNaN(mainThread))
			{
				attributes.Add(new KeyValuePair<string, TelemetryValue>("unity.frame.main_thread.time", mainThread));
			}
			if (allocated >= 0)
			{
				attributes.Add(new KeyValuePair<string, TelemetryValue>("unity.gc.frame_allocated", allocated));
			}
			AddLocation(attributes);
			Add(m_Hitch, "hitch", attributes);
		}

		void Position()
		{
			if (!m_Position.IsEnabled(TelemetrySeverity.Info))
			{
				return;
			}
			var attributes = new List<KeyValuePair<string, TelemetryValue>>();
			if (AddLocation(attributes))
			{
				Add(m_Position, "position", attributes);
			}
		}

		/// <summary>Queues the record with what the game is doing.</summary>
		void Add(TelemetryLogger logger, string body, List<KeyValuePair<string, TelemetryValue>> attributes)
		{
			foreach (var pair in m_Annotations.Pairs())
			{
				attributes.Add(new KeyValuePair<string, TelemetryValue>(pair.Key, pair.Value));
			}
			m_Logs.Add(new TelemetryLogs.Entry
			{
				Logger = logger,
				Severity = TelemetrySeverity.Info,
				Body = body,
				Extra = attributes.ToArray(),
			});
		}

		bool AddLocation(List<KeyValuePair<string, TelemetryValue>> attributes)
		{
			if (m_Location == null || !m_Location.TryGetLocation(out var map, out var x, out var y, out var z))
			{
				return false;
			}
			attributes.Add(new KeyValuePair<string, TelemetryValue>("dexc.map", map ?? string.Empty));
			attributes.Add(new KeyValuePair<string, TelemetryValue>("dexc.position.x", x));
			attributes.Add(new KeyValuePair<string, TelemetryValue>("dexc.position.y", y));
			attributes.Add(new KeyValuePair<string, TelemetryValue>("dexc.position.z", z));
			return true;
		}
	}
}
