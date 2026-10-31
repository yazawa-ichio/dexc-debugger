using System;

namespace Dexc
{
	/// <summary>
	/// Configuration supplied while starting a Dexc runtime session.
	/// </summary>
	public sealed class DexcDebuggerOptions
	{
		internal DexcDebuggerOptions()
		{
			Command = new CommandOptions();
			History = new HistoryOptions();
			Diagnostic = new DiagnosticOptions();
			Inspector = new InspectorOptions();
			Display = new DisplayOptions();
			Query = new QueryOptions();
			RuntimeUI = new RuntimeUIOptions();
		}

		/// <summary>Gets command configuration.</summary>
		public CommandOptions Command { get; }

		/// <summary>Gets history configuration.</summary>
		public HistoryOptions History { get; }

		/// <summary>Diagnostic limits. Internal: package defaults apply; tests may override.</summary>
		internal DiagnosticOptions Diagnostic { get; }

		/// <summary>Inspector limits. Internal: package defaults apply; tests may override.</summary>
		internal InspectorOptions Inspector { get; }

		/// <summary>Display limits. Internal: package defaults apply; tests may override.</summary>
		internal DisplayOptions Display { get; }

		/// <summary>Query limits. Internal: package defaults apply; tests may override.</summary>
		internal QueryOptions Query { get; }

		/// <summary>Gets Runtime UI configuration.</summary>
		public RuntimeUIOptions RuntimeUI { get; }

		internal DexcImmutableOptions ToImmutable()
		{
			ValidateNonNegative(History.CapacityPerFeature, nameof(History.CapacityPerFeature));
			ValidateNonNegative(Diagnostic.OperationCapacity, nameof(Diagnostic.OperationCapacity));
			ValidateNonNegative(Inspector.MaxWatchCount, nameof(Inspector.MaxWatchCount));
			ValidateNonNegative(Inspector.MaxWatchUpdatesPerFrame, nameof(Inspector.MaxWatchUpdatesPerFrame));
			ValidateNonNegative(Display.MaxSubjectCount, nameof(Display.MaxSubjectCount));
			ValidateNonNegative(Display.MaxMetadataPerSubject, nameof(Display.MaxMetadataPerSubject));
			ValidateNonNegative(Display.MaxVisualsPerSubject, nameof(Display.MaxVisualsPerSubject));
			ValidateNonNegative(Display.MaxVisualsPerFrame, nameof(Display.MaxVisualsPerFrame));
			ValidateNonNegative(Display.MaxPolylinePointCount, nameof(Display.MaxPolylinePointCount));
			ValidateAtLeastOne(Query.MaxPageSize, nameof(Query.MaxPageSize));
			ValidateAtLeastOne(Query.MaxHierarchyChildren, nameof(Query.MaxHierarchyChildren));
			ValidateNonNegative(Query.MaxSearchResults, nameof(Query.MaxSearchResults));
			if (!Enum.IsDefined(typeof(DebugCommandAccess), Command.DefaultAccess))
			{
				throw new ArgumentException(
					"Command.DefaultAccess is not a defined DebugCommandAccess value.",
					nameof(Command.DefaultAccess)
				);
			}

			var runtimeUISkin = DexcRuntimeUISkinSnapshot.Create(RuntimeUI.Skin);
			return new DexcImmutableOptions(
				Command.DefaultAccess,
				History.CapacityPerFeature,
				Diagnostic.OperationCapacity,
				Inspector.MaxWatchCount,
				Inspector.MaxWatchUpdatesPerFrame,
				Display.MaxSubjectCount,
				Display.MaxMetadataPerSubject,
				Display.MaxVisualsPerSubject,
				Display.MaxVisualsPerFrame,
				Display.MaxPolylinePointCount,
				Query.MaxPageSize,
				Query.MaxHierarchyChildren,
				Query.MaxSearchResults,
				runtimeUISkin
			);
		}

		private static void ValidateNonNegative(long value, string name)
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException(name, value, "The value must be zero or greater.");
			}
		}

		private static void ValidateAtLeastOne(int value, string name)
		{
			if (value < 1)
			{
				throw new ArgumentOutOfRangeException(name, value, "The value must be one or greater.");
			}
		}

		public sealed class CommandOptions
		{
			internal CommandOptions()
			{
				DefaultAccess = DebugCommandAccess.Lock;
			}

			/// <summary>Gets or sets the project default command access.</summary>
			public DebugCommandAccess DefaultAccess { get; set; }
		}

		public sealed class HistoryOptions
		{
			internal HistoryOptions()
			{
				CapacityPerFeature = 100;
			}

			/// <summary>Gets or sets the per-feature history capacity.</summary>
			public int CapacityPerFeature { get; set; }
		}

		internal sealed class DiagnosticOptions
		{
			internal DiagnosticOptions()
			{
				OperationCapacity = 100;
			}

			/// <summary>Gets or sets the operation diagnostic count capacity.</summary>
			public int OperationCapacity { get; set; }
		}

		internal sealed class InspectorOptions
		{
			internal InspectorOptions()
			{
				MaxWatchCount = 64;
				MaxWatchUpdatesPerFrame = 64;
			}

			/// <summary>Gets or sets the maximum number of watches.</summary>
			public int MaxWatchCount { get; set; }

			/// <summary>Gets or sets the per-frame watch update budget.</summary>
			public int MaxWatchUpdatesPerFrame { get; set; }
		}

		internal sealed class DisplayOptions
		{
			internal DisplayOptions()
			{
				MaxSubjectCount = 256;
				MaxMetadataPerSubject = 32;
				MaxVisualsPerSubject = 256;
				MaxVisualsPerFrame = 2048;
				MaxPolylinePointCount = 256;
			}

			/// <summary>Gets or sets the maximum number of display subjects.</summary>
			public int MaxSubjectCount { get; set; }

			/// <summary>Gets or sets the per-subject metadata capacity.</summary>
			public int MaxMetadataPerSubject { get; set; }

			/// <summary>Gets or sets the per-subject visual capacity.</summary>
			public int MaxVisualsPerSubject { get; set; }

			/// <summary>Gets or sets the per-frame visual capacity.</summary>
			public int MaxVisualsPerFrame { get; set; }

			/// <summary>Gets or sets the maximum points in one polyline.</summary>
			public int MaxPolylinePointCount { get; set; }
		}

		internal sealed class QueryOptions
		{
			internal QueryOptions()
			{
				MaxPageSize = 100;
				MaxHierarchyChildren = 100;
				MaxSearchResults = 100;
			}

			/// <summary>Gets or sets the maximum query page size.</summary>
			public int MaxPageSize { get; set; }

			/// <summary>Gets or sets the direct hierarchy child limit.</summary>
			public int MaxHierarchyChildren { get; set; }

			/// <summary>Gets or sets the maximum search result count.</summary>
			public int MaxSearchResults { get; set; }
		}

		public sealed class RuntimeUIOptions
		{
			internal RuntimeUIOptions() { }

			/// <summary>
			/// Gets or sets the optional Runtime UI Skin copied into the new session.
			/// A null value uses the code-only shell and disables Skin-dependent World Visual rendering.
			/// </summary>
			public DexcRuntimeUISkin Skin { get; set; }
		}
	}

	internal sealed class DexcImmutableOptions
	{
		internal DexcImmutableOptions(
			DebugCommandAccess defaultAccess,
			int historyCapacityPerFeature,
			int operationCapacity,
			int maxWatchCount,
			int maxWatchUpdatesPerFrame,
			int maxSubjectCount,
			int maxMetadataPerSubject,
			int maxVisualsPerSubject,
			int maxVisualsPerFrame,
			int maxPolylinePointCount,
			int maxPageSize,
			int maxHierarchyChildren,
			int maxSearchResults,
			DexcRuntimeUISkinSnapshot runtimeUISkin
		)
		{
			DefaultAccess = defaultAccess;
			HistoryCapacityPerFeature = historyCapacityPerFeature;
			OperationCapacity = operationCapacity;
			MaxWatchCount = maxWatchCount;
			MaxWatchUpdatesPerFrame = maxWatchUpdatesPerFrame;
			MaxSubjectCount = maxSubjectCount;
			MaxMetadataPerSubject = maxMetadataPerSubject;
			MaxVisualsPerSubject = maxVisualsPerSubject;
			MaxVisualsPerFrame = maxVisualsPerFrame;
			MaxPolylinePointCount = maxPolylinePointCount;
			MaxPageSize = maxPageSize;
			MaxHierarchyChildren = maxHierarchyChildren;
			MaxSearchResults = maxSearchResults;
			RuntimeUISkin = runtimeUISkin;
		}

		internal DebugCommandAccess DefaultAccess { get; }
		internal int HistoryCapacityPerFeature { get; }
		internal int OperationCapacity { get; }
		internal int MaxWatchCount { get; }
		internal int MaxWatchUpdatesPerFrame { get; }
		internal int MaxSubjectCount { get; }
		internal int MaxMetadataPerSubject { get; }
		internal int MaxVisualsPerSubject { get; }
		internal int MaxVisualsPerFrame { get; }
		internal int MaxPolylinePointCount { get; }
		internal int MaxPageSize { get; }
		internal int MaxHierarchyChildren { get; }
		internal int MaxSearchResults { get; }
		internal DexcRuntimeUISkinSnapshot RuntimeUISkin { get; }
	}
}
