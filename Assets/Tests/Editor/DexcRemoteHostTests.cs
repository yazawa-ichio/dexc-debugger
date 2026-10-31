using System.Linq;
using Dexc;
using NUnit.Framework;
using UnityEngine;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	[DebugCommandAttribute("ZZTests/Remote/Echo")]
	public sealed class RemoteHostEchoCommand : DebugCommand<string>
	{
		public InputString Text = new InputString("hello");

		protected override string Execute() => Text.Value;
	}

	[DebugCommandAttribute("ZZTests/Remote/Move")]
	public sealed class RemoteHostMoveCommand : DebugCommand<string>
	{
		public InputFloat Speed = new InputFloat(1f);
		public InputVector3 Direction = new InputVector3(Vector3.zero);

		protected override string Execute() =>
			Speed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + Direction.Value.ToString("F1");
	}

	[DebugCommandAttribute("ZZTests/Remote/Slow")]
	public sealed class RemoteHostSlowCommand : AsyncDebugCommand<string>
	{
		protected override async System.Threading.Tasks.ValueTask<string> ExecuteAsync(System.Threading.CancellationToken token)
		{
			await System.Threading.Tasks.Task.Delay(1, token);
			return "slow";
		}
	}

	[DebugCommandAttribute("ZZTests/Remote/NeedsReason")]
	public sealed class RemoteHostRefusingCommand : DebugCommand<string>
	{
		public InputString Reason = new InputString(string.Empty);

		protected override string Execute() => Reason.Value;

		protected override void Validate(DebugCommandValidation validation)
		{
			if (string.IsNullOrEmpty(Reason.Value))
				validation.Error(Reason, "reason-missing", "Say why first.");
		}
	}

	public sealed class RemoteHostInspectorComponent : MonoBehaviour
	{
		[DebugField]
		public int Charge = 7;

		[DebugField]
		public string Callsign = "vector";
	}

	public sealed class RemoteHostSettings : DebugSettingSet<RemoteHostSettings>
	{
		[DebugSetting("ZZTests/Remote/Volume")]
		[DebugRange(0, 10)]
		public int Volume { get; private set; } = 4;

		[DebugPreset("zztests-remote-loud", "Loud")]
		public static RemoteHostSettings Loud => new RemoteHostSettings { Volume = 9 };
	}

	/// <summary>
	/// The host is a frame in and a frame out. These tests hand it text and read the text back, with no transport of any
	/// kind, which is what the separation is for.
	/// </summary>
	public sealed class DexcRemoteHostTests
	{
		private DexcRemoteHost m_Host;

		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.ResetForTests();
			m_Host = new DexcRemoteHost();
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
		}

		private static string Frame(string method, string parameters = null)
		{
			var tail = parameters == null ? string.Empty : ",\"params\":" + parameters;
			return "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + method + "\"" + tail + "}";
		}

		private DexcJsonValue Call(string method, string parameters = null)
		{
			var read = DexcJson.Parse(m_Host.Handle(Frame(method, parameters)));
			Assert.That(read.TryGetMember("error", out var error), Is.False, error == null ? string.Empty : error.ToString());
			Assert.That(read.TryGetMember("result", out var result), Is.True);
			return result;
		}

		private static long Number(DexcJsonValue value, string name)
		{
			Assert.That(value.TryGetMember(name, out var member), Is.True, "member \"" + name + "\" is present");
			Assert.That(member.TryGetInt64(out var number), Is.True, "member \"" + name + "\" is a whole number");
			return number;
		}

		private static string Reason(string frame)
		{
			var read = DexcJson.Parse(frame);
			Assert.That(read.TryGetMember("error", out var error), Is.True, "the answer carries an error");
			Assert.That(error.TryGetMember("data", out var data), Is.True);
			Assert.That(data.TryGetMember("reason", out var reason), Is.True);
			Assert.That(reason.TryGetString(out var text), Is.True);
			return text;
		}

		private static string Text(DexcJsonValue value, string name)
		{
			Assert.That(value.TryGetMember(name, out var member), Is.True, "member \"" + name + "\" is present");
			Assert.That(member.TryGetString(out var text), Is.True, "member \"" + name + "\" is a string");
			return text;
		}

		[Test]
		public void HelloAnswersBeforeAnySessionExists()
		{
			var result = Call("hello");

			Assert.That(Text(result, "protocolVersion"), Is.EqualTo(DexcRemoteHost.ProtocolVersion));
			Assert.That(Text(result, "encoding"), Is.EqualTo("json"));
			Assert.That(result.TryGetMember("session", out var session), Is.True);
			Assert.That(Text(session, "state"), Is.EqualTo("not-initialized"));
		}

		/// <summary>
		/// Not having a session is the game's business, not a broken request. The answer says so where a caller reads
		/// results, not where it reads failures.
		/// </summary>
		[Test]
		public void AFeatureWithoutASessionIsDisabledRatherThanAnError()
		{
			var result = Call("command.list");

			Assert.That(Text(result, "status"), Is.EqualTo("disabled"));
		}

		// What follows needs a session, and a session needs the symbol. Without it every operation answers "disabled",
		// which is what the test above checks; running these as well would only assert that a disabled build is disabled.
#if DEXC_DEBUGGER
		[Test]
		public void TheEpochMovesWhenTheSessionIsReplaced()
		{
			DebuggerRuntime.Initialize();
			var before = Number(Call("session.status"), "epoch");

			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			var second = Call("session.status");

			Assert.That(Number(second, "epoch"), Is.GreaterThan(before), "a new session is a new epoch");
			Assert.That(DexcJson.Write(second), Does.Not.Contain("session:"), "the session's own id stays inside");
		}

		[Test]
		public void CommandsAreListedByPath()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.list", "{\"pathPrefix\":\"ZZTests/Remote\",\"count\":20}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(result.TryGetMember("items", out var items), Is.True);
			var paths = items.Items.Select(item => Text(item, "path")).ToArray();
			Assert.That(paths, Does.Contain("ZZTests/Remote/Echo"));
			Assert.That(result.TryGetMember("page", out var page), Is.True);
			Assert.That(page.TryGetMember("totalCount", out _), Is.True);
		}

		[Test]
		public void ACommandIsDescribedByItsPathAndFieldNames()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.describe", "{\"path\":\"ZZTests/Remote/Echo\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(result, "path"), Is.EqualTo("ZZTests/Remote/Echo"));
			Assert.That(result.TryGetMember("fields", out var fields), Is.True);
			Assert.That(fields.Items.Select(item => Text(item, "name")), Does.Contain("Text"));
			Assert.That(DexcJson.Write(result), Does.Not.Contain("InputType\""), "the CLR type stays inside");
		}

		[Test]
		public void AnUnknownPathIsNotFoundRatherThanAnError()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.describe", "{\"path\":\"ZZTests/Remote/Nope\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		[Test]
		public void ASettingIsReadByPath()
		{
			DebuggerRuntime.Initialize();

			var result = Call("setting.read", "{\"path\":\"ZZTests/Remote/Volume\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(result.TryGetMember("value", out var value), Is.True);
			Assert.That(value.TryGetInt64(out var number), Is.True);
			Assert.That(number, Is.EqualTo(4L));
		}

		[Test]
		public void SettingsAreListed()
		{
			DebuggerRuntime.Initialize();

			var result = Call("setting.list", "{\"pathPrefix\":\"ZZTests/Remote\",\"count\":20}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(result.TryGetMember("items", out var items), Is.True);
			Assert.That(items.Items.Select(item => Text(item, "path")), Does.Contain("ZZTests/Remote/Volume"));
		}

		[Test]
		public void PresetsAreListedByTheirDeclaredId()
		{
			DebuggerRuntime.Initialize();

			var result = Call("setting.presets", "{\"pathPrefix\":\"ZZTests/Remote\",\"count\":20}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(result.TryGetMember("items", out var items), Is.True);
			var loud = items.Items.Single();
			Assert.That(Text(loud, "id"), Is.EqualTo("zztests-remote-loud"));
			Assert.That(Text(loud, "displayName"), Is.EqualTo("Loud"));
		}

		[Test]
		public void APresetPreviewShowsTheDifferenceWithoutApplyingIt()
		{
			DebuggerRuntime.Initialize();

			var result = Call("setting.previewPreset", "{\"presetId\":\"zztests-remote-loud\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(result.TryGetMember("differences", out var differences), Is.True);
			var volume = differences.Items.Single();
			Assert.That(Text(volume, "path"), Is.EqualTo("ZZTests/Remote/Volume"));
			Assert.That(Number(volume, "current"), Is.EqualTo(4L));
			Assert.That(Number(volume, "preset"), Is.EqualTo(9L));
			Assert.That(Number(Call("setting.read", "{\"path\":\"ZZTests/Remote/Volume\"}"), "value"), Is.EqualTo(4L));
			Assert.That(Text(Call("setting.previewPreset", "{\"presetId\":\"nope\"}"), "status"), Is.EqualTo("not-found"));
		}

		[Test]
		public void ACommandRunsAndAnswersWithItsOutput()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.execute", "{\"path\":\"ZZTests/Remote/Echo\",\"inputs\":{\"Text\":\"carried\"}}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(result, "executionStatus"), Is.EqualTo("Succeeded"));
			Assert.That(Text(result, "output"), Is.EqualTo("carried"));
		}

		/// <summary>A path lookup reads pages the session allows; it does not ask for a page larger than the session's limit.</summary>
		[Test]
		public void ACommandIsFoundUnderASmallPageLimit()
		{
			DebuggerRuntime.Initialize(options => options.Query.MaxPageSize = 2);

			var result = Call("command.execute", "{\"path\":\"ZZTests/Remote/Echo\",\"inputs\":{\"Text\":\"paged\"}}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(result, "output"), Is.EqualTo("paged"));
		}

		/// <summary>JSON writes 2.0 as 2, so a whole number reaches a floating point input as an integer; the input's schema reads it.</summary>
		[Test]
		public void AWholeNumberFillsAFloatingPointInput()
		{
			DebuggerRuntime.Initialize();

			var result = Call(
				"command.execute",
				"{\"path\":\"ZZTests/Remote/Move\",\"inputs\":{\"Speed\":2.0,\"Direction\":{\"x\":1,\"y\":0.0,\"z\":-3}}}"
			);

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(result, "output"), Is.EqualTo("2 (1.0, 0.0, -3.0)"));
		}

		[Test]
		public void ACommandRunsWithoutInputs()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.execute", "{\"path\":\"ZZTests/Remote/Echo\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(result, "output"), Is.EqualTo("hello"), "the declared value is what runs");
		}

		[Test]
		public void AnUnknownInputNameIsNotFound()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.execute", "{\"path\":\"ZZTests/Remote/Echo\",\"inputs\":{\"Nope\":1}}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		/// <summary>
		/// This carrier hands the work to the main thread and waits for it, and an async command finishes on that same
		/// thread. It says so rather than hanging.
		/// </summary>
		[Test]
		public void AnAsyncCommandIsRefusedByThisCarrier()
		{
			DebuggerRuntime.Initialize();

			var result = Call("command.execute", "{\"path\":\"ZZTests/Remote/Slow\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("unsupported"));
		}

		/// <summary>
		/// A refusal carries the command's own reasons. Without them a tool outside the process only learns that it was
		/// turned down, and a command ends up skipping its validation just to be able to say why in its output.
		/// </summary>
		[Test]
		public void ARefusedCommandSaysWhy()
		{
			DebuggerRuntime.Initialize();

			var refused = Call("command.execute", "{\"path\":\"ZZTests/Remote/NeedsReason\"}");

			Assert.That(Text(refused, "status"), Is.EqualTo("rejected"));
			Assert.That(refused.TryGetMember("issues", out var issues), Is.True);
			var issue = issues.Items.Single();
			Assert.That(Text(issue, "code"), Is.EqualTo("reason-missing"));
			Assert.That(Text(issue, "message"), Is.EqualTo("Say why first."));
			Assert.That(Text(issue, "field"), Is.EqualTo("Reason"), "named the way the inputs are given");

			var accepted = Call("command.execute", "{\"path\":\"ZZTests/Remote/NeedsReason\",\"inputs\":{\"Reason\":\"testing\"}}");

			Assert.That(Text(accepted, "status"), Is.EqualTo("succeeded"));
			Assert.That(Text(accepted, "output"), Is.EqualTo("testing"));
		}

		/// <summary>A page larger than the session allows is refused, not quietly cut down to size.</summary>
		[Test]
		public void AnOversizedPageIsRefused()
		{
			DebuggerRuntime.Initialize();

			var reason = Reason(m_Host.Handle(Frame("command.list", "{\"count\":100000}")));

			Assert.That(reason, Is.EqualTo("page-size-exceeded"));
		}
#endif

		[Test]
		public void AParameterOfTheWrongShapeIsRefused()
		{
			DebuggerRuntime.Initialize();

			var reason = Reason(m_Host.Handle(Frame("command.list", "{\"pathPrefix\":7}")));

			Assert.That(reason, Is.EqualTo("invalid-params"));
		}

		[Test]
		public void AnUnknownMethodIsAnswered()
		{
			var read = DexcJson.Parse(m_Host.Handle(Frame("command.nope")));

			Assert.That(read.TryGetMember("error", out var error), Is.True);
			Assert.That(Number(error, "code"), Is.EqualTo(DexcRemoteErrorCode.MethodNotFound));
			Assert.That(Reason(m_Host.Handle(Frame("command.nope"))), Is.EqualTo("unknown-method"));
		}

#if DEXC_DEBUGGER
		/// <summary>
		/// The whole Inspector path in one go: a listing gives an instance id, and everything after it is asked for by that
		/// id. A handle never leaves, which is the point - it means nothing to a caller outside the process.
		/// </summary>
		[Test]
		public void AnObjectIsWalkedByTheEntityIdTheListingGave()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostInspected");
			try
			{
				owner.AddComponent<RemoteHostInspectorComponent>();
				DebugInspector.Register("ZZTests/Remote/Inspected", owner, "inspected");

				var registered = Call("inspector.registered", "{\"pathPrefix\":\"ZZTests/Remote\",\"count\":20}");

				Assert.That(Text(registered, "status"), Is.EqualTo("succeeded"));
				Assert.That(registered.TryGetMember("items", out var items), Is.True);
				var entry = items.Items.Single(item => Text(item, "path") == "ZZTests/Remote/Inspected");
				var entityId = Number(entry, "entityId");
				Assert.That(entityId, Is.EqualTo((long)DebugInspector.EntityValue(owner)), "the listing names the object by its entity id");

				var components = Call("inspector.components", "{\"entityId\":" + entityId + "}");

				Assert.That(Text(components, "status"), Is.EqualTo("succeeded"));
				Assert.That(components.TryGetMember("items", out var declared), Is.True);
				Assert.That(declared.Items.Select(item => Text(item, "displayName")), Does.Contain("RemoteHostInspectorComponent"));

				var fields = Call(
					"inspector.fields",
					"{\"entityId\":" + entityId + ",\"typeName\":\"RemoteHostInspectorComponent\",\"count\":20}"
				);

				Assert.That(fields.TryGetMember("items", out var named), Is.True);
				Assert.That(named.Items.Select(item => Text(item, "name")), Does.Contain("Charge"));

				var read = Call(
					"inspector.read",
					"{\"entityId\":" + entityId + ",\"typeName\":\"RemoteHostInspectorComponent\",\"fields\":[\"Charge\",\"Callsign\"]}"
				);

				Assert.That(Text(read, "status"), Is.EqualTo("succeeded"));
				Assert.That(read.TryGetMember("items", out var values), Is.True);
				Assert.That(Number(values.Items.Single(item => Text(item, "name") == "Charge"), "value"), Is.EqualTo(7L));
				Assert.That(Text(values.Items.Single(item => Text(item, "name") == "Callsign"), "value"), Is.EqualTo("vector"));
				Assert.That(DexcJson.Write(read), Does.Not.Contain("inspector-target:"), "the handle stays inside");
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void AnEntityIdFromNoRunIsNotFound()
		{
			DebuggerRuntime.Initialize();

			var result = Call("inspector.components", "{\"entityId\":987654321987654}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		/// <summary>
		/// An id past what a double holds exactly leaves as text, so it has to come back in as text. A caller hands back
		/// what it was given without having to know which of the two forms that was.
		/// </summary>
		[Test]
		public void AnEntityIdIsReadAsTextAsWellAsANumber()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostInspected");
			try
			{
				owner.AddComponent<RemoteHostInspectorComponent>();
				DebugInspector.Register("ZZTests/Remote/Inspected", owner, "inspected");

				var result = Call("inspector.components", "{\"entityId\":\"" + DebugInspector.EntityValue(owner) + "\"}");

				Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
				Assert.That(result.TryGetMember("items", out var items), Is.True);
				Assert.That(items.Items.Select(item => Text(item, "displayName")), Does.Contain("RemoteHostInspectorComponent"));
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void AnEntityIdThatIsNotANumberIsRefused()
		{
			DebuggerRuntime.Initialize();

			var reason = Reason(m_Host.Handle(Frame("inspector.components", "{\"entityId\":\"nope\"}")));

			Assert.That(reason, Is.EqualTo("invalid-params"));
		}

		[Test]
		public void AFieldTheComponentDoesNotDeclareIsNotFound()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostInspected");
			try
			{
				owner.AddComponent<RemoteHostInspectorComponent>();
				DebugInspector.Register("ZZTests/Remote/Inspected", owner, "inspected");
				var entityId = DebugInspector.EntityValue(owner);

				var result = Call(
					"inspector.read",
					"{\"entityId\":" + entityId + ",\"typeName\":\"RemoteHostInspectorComponent\",\"fields\":[\"Nope\"]}"
				);

				Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void AComponentTheObjectDoesNotHaveIsNotFound()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostInspected");
			try
			{
				DebugInspector.Register("ZZTests/Remote/Inspected", owner, "inspected");

				var result = Call(
					"inspector.fields",
					"{\"entityId\":" + DebugInspector.EntityValue(owner) + ",\"typeName\":\"Rigidbody\"}"
				);

				Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		/// <summary>Walking a scene is what makes the objects on it nameable afterwards.</summary>
		[Test]
		public void SceneChildrenCarryTheEntityIdsToAskAbout()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostInScene");
			try
			{
				var scenes = Call("inspector.scenes");

				Assert.That(Text(scenes, "status"), Is.EqualTo("succeeded"));
				Assert.That(scenes.TryGetMember("items", out var loaded), Is.True);
				// By the token, because a scene the Editor has not saved has neither a name nor a path.
				var scene = Text(loaded.Items.First(), "scene");

				var children = Call("inspector.children", "{\"scene\":\"" + scene + "\",\"count\":50}");

				Assert.That(Text(children, "status"), Is.EqualTo("succeeded"));
				Assert.That(children.TryGetMember("items", out var items), Is.True);
				var entry = items.Items.Single(item => Text(item, "label") == "RemoteHostInScene");
				Assert.That(Number(entry, "entityId"), Is.EqualTo((long)DebugInspector.EntityValue(owner)));
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void ASceneThatIsNotLoadedIsNotFound()
		{
			DebuggerRuntime.Initialize();

			var result = Call("inspector.children", "{\"scene\":\"ZZTests/Remote/NoSuchScene\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		[Test]
		public void ADisplayFrameIsNotFoundUntilOneHasBeenDrawn()
		{
			DebuggerRuntime.Initialize();

			var result = Call("display.frame");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		[Test]
		public void ADisplayFrameCarriesItsSubjectsAndGlobals()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostDisplayOwner");
			try
			{
				var subject = DebugDisplay.CreateSubject("ZZTests/Remote", owner);
				subject.SetMetadata("Health", 10);
				subject.DrawLine("Reach", Vector3.zero, Vector3.right, Color.green);
				DebugDisplay.DrawLine("ZZTests/Remote/Global", Vector3.zero, Vector3.up, Color.white);
				DebuggerRuntime.CurrentSession.Display.Tick();

				var result = Call("display.frame");

				Assert.That(Text(result, "status"), Is.EqualTo("succeeded"));
				Assert.That(result.TryGetMember("subjects", out var subjects), Is.True);
				var carried = subjects.Items.Single(item => Text(item, "category") == "ZZTests/Remote");
				Assert.That(carried.TryGetMember("metadata", out var metadata), Is.True);
				Assert.That(metadata.Items.Select(item => Text(item, "name")), Does.Contain("Health"));
				Assert.That(carried.TryGetMember("visuals", out var visuals), Is.True);
				var line = visuals.Items.Single(item => Text(item, "name") == "Reach");
				Assert.That(Text(line, "kind"), Is.EqualTo("Line"));
				Assert.That(line.TryGetMember("end", out var end), Is.True);
				Assert.That(Real(end, "x"), Is.EqualTo(1d).Within(0.0001d));
				Assert.That(result.TryGetMember("globals", out var globals), Is.True);
				Assert.That(globals.Items.Select(item => Text(item, "name")), Does.Contain("ZZTests/Remote/Global"));
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		/// <summary>The owning object is described. The handle it holds is not a name anything outside can use.</summary>
		[Test]
		public void ADisplaySubjectDescribesItsOwnerWithoutHandingOutAHandle()
		{
			DebuggerRuntime.Initialize();
			var owner = new GameObject("RemoteHostDisplayOwner");
			try
			{
				var subject = DebugDisplay.CreateSubject("ZZTests/Remote", owner);
				subject.DrawLine("Reach", Vector3.zero, Vector3.right, Color.green);
				DebuggerRuntime.CurrentSession.Display.Tick();

				var result = Call("display.frame");

				Assert.That(result.TryGetMember("subjects", out var subjects), Is.True);
				var carried = subjects.Items.Single(item => Text(item, "category") == "ZZTests/Remote");
				Assert.That(carried.TryGetMember("owner", out var ownerMember), Is.True);
				Assert.That(Text(ownerMember, "hierarchyPath"), Does.Contain("RemoteHostDisplayOwner"));
				Assert.That(DexcJson.Write(result), Does.Not.Contain("object:"), "the object handle stays inside");
			}
			finally
			{
				Object.DestroyImmediate(owner);
			}
		}

		[Test]
		public void ADiagnosticIsFetchedByTheIdTheListingGave()
		{
			DebuggerRuntime.Initialize();
			Record(DebugFeature.Setting, "remote-diagnostic");

			var listed = Call("diagnostics.list", "{\"count\":20}");

			Assert.That(Text(listed, "status"), Is.EqualTo("succeeded"));
			var id = Text(Listed(listed, "remote-diagnostic"), "diagnosticId");

			var detail = Call("diagnostics.detail", "{\"diagnosticId\":\"" + id + "\"}");

			Assert.That(Text(detail, "status"), Is.EqualTo("succeeded"));
			Assert.That(detail.TryGetMember("summary", out var summary), Is.True);
			Assert.That(Text(summary, "code"), Is.EqualTo("remote-diagnostic"));
		}

		/// <summary>A diagnostic id comes from a listing. One that was never handed out cannot be composed into existence.</summary>
		[Test]
		public void ADiagnosticIdThatWasNeverListedIsNotFound()
		{
			DebuggerRuntime.Initialize();
			Record(DebugFeature.Setting, "remote-diagnostic");

			var result = Call("diagnostics.detail", "{\"diagnosticId\":\"diagnostic:1\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"));
		}

		[Test]
		public void ADiagnosticIdFromAnEarlierRunIsNotFound()
		{
			DebuggerRuntime.Initialize();
			Record(DebugFeature.Setting, "remote-diagnostic");
			var id = Text(Listed(Call("diagnostics.list", "{\"count\":20}"), "remote-diagnostic"), "diagnosticId");

			DebuggerRuntime.ResetForTests();
			DebuggerRuntime.Initialize();
			var result = Call("diagnostics.detail", "{\"diagnosticId\":\"" + id + "\"}");

			Assert.That(Text(result, "status"), Is.EqualTo("not-found"), "a new run means what was held is about something else");
		}

		[Test]
		public void DiagnosticsAreFilteredByFeature()
		{
			DebuggerRuntime.Initialize();
			Record(DebugFeature.Setting, "remote-setting");
			Record(DebugFeature.Inspector, "remote-inspector");

			var result = Call("diagnostics.list", "{\"features\":\"Setting\",\"count\":20}");

			Assert.That(result.TryGetMember("items", out var items), Is.True);
			var codes = items.Items.Select(item => Text(item, "code")).ToArray();
			Assert.That(codes, Does.Contain("remote-setting"));
			Assert.That(codes, Does.Not.Contain("remote-inspector"));
		}

		/// <summary>
		/// A filter this does not understand is refused as a bad parameter, not reported as a page that was too large.
		/// The query reports both the same way, so the filter is read before the query is built.
		/// </summary>
		[Test]
		public void AFilterThatIsNotAFeatureIsRefused()
		{
			DebuggerRuntime.Initialize();

			var reason = Reason(m_Host.Handle(Frame("diagnostics.list", "{\"features\":\"Nope\"}")));

			Assert.That(reason, Is.EqualTo("invalid-params"));
		}

		[Test]
		public void AnOversizedDiagnosticPageIsRefused()
		{
			DebuggerRuntime.Initialize();

			var reason = Reason(m_Host.Handle(Frame("diagnostics.list", "{\"count\":100000}")));

			Assert.That(reason, Is.EqualTo("page-size-exceeded"));
		}

		private static void Record(DebugFeature feature, string code)
		{
			DebuggerRuntime.CurrentSession.Diagnostics.AddOperation(
				DebugDiagnosticSeverity.Error,
				feature,
				"write",
				code,
				"Something the game recorded.",
				"ZZTests/Remote/Volume",
				"exec"
			);
		}

		private static DexcJsonValue Listed(DexcJsonValue result, string code)
		{
			Assert.That(result.TryGetMember("items", out var items), Is.True);
			return items.Items.Single(item => Text(item, "code") == code);
		}

		private static double Real(DexcJsonValue value, string name)
		{
			Assert.That(value.TryGetMember(name, out var member), Is.True, "member \"" + name + "\" is present");
			Assert.That(member.TryGetDouble(out var number), Is.True, "member \"" + name + "\" is a number");
			return number;
		}

		/// <summary>Two hosts answer about the same features without sharing what either has seen.</summary>
		[Test]
		public void TwoHostsKeepTheirOwnState()
		{
			DebuggerRuntime.Initialize();
			var other = new DexcRemoteHost();

			var mine = DexcJson.Parse(m_Host.Handle(Frame("session.status")));
			var theirs = DexcJson.Parse(other.Handle(Frame("session.status")));

			Assert.That(mine.TryGetMember("result", out var mineResult), Is.True);
			Assert.That(theirs.TryGetMember("result", out var theirsResult), Is.True);
			Assert.That(Text(mineResult, "state"), Is.EqualTo("active"));
			Assert.That(Text(theirsResult, "state"), Is.EqualTo("active"));
		}
#endif
	}

	/// <summary>
	/// The mapping is the only place a DebugValue leaves the process. Out it becomes plain objects, which whoever is
	/// carrying the answer serializes; it has to come back the same, including the digits a double would round off.
	/// </summary>
	public sealed class DexcRemoteValueTests
	{
		private static DebugValue RoundTrip(DebugValue value)
		{
			var carried = DexcJson.Write(DexcRemoteJsonWriter.From(DexcRemoteValue.ToObject(value)));
			var arrived = DexcRemoteJsonWriter.ToObject(DexcJson.Parse(carried));
			Assert.That(DexcRemoteValueReader.TryFromObject(arrived, out var read, out var error), Is.True, error);
			return read;
		}

		[Test]
		public void EveryKindComesBack()
		{
			var original = DebugValue.FromObject(
				new DebugValueMember("flag", DebugValue.From(true)),
				new DebugValueMember("count", DebugValue.From(-7L)),
				new DebugValueMember("ratio", DebugValue.From(0.25)),
				new DebugValueMember("name", DebugValue.From("dexc")),
				new DebugValueMember("nothing", DebugValue.Null),
				new DebugValueMember("items", DebugValue.FromArray(DebugValue.From(1L), DebugValue.From("two")))
			);

			var read = RoundTrip(original);

			Assert.That(read.Kind, Is.EqualTo(DebugValueKind.Object));
			Assert.That(read, Is.EqualTo(original));
		}

		/// <summary>A value becomes objects a serializer already understands, not a string holding JSON.</summary>
		[Test]
		public void AValueBecomesPlainObjects()
		{
			var carried = DexcRemoteValue.ToObject(
				DebugValue.FromObject(
					new DebugValueMember("name", DebugValue.From("dexc")),
					new DebugValueMember("items", DebugValue.FromArray(DebugValue.From(1L)))
				)
			);

			Assert.That(carried, Is.InstanceOf<System.Collections.Generic.IDictionary<string, object>>());
			var map = (System.Collections.Generic.IDictionary<string, object>)carried;
			Assert.That(map["name"], Is.EqualTo("dexc"));
			Assert.That(map["items"], Is.InstanceOf<System.Collections.Generic.List<object>>());
		}

		/// <summary>
		/// An unsigned value past what a double holds exactly is carried as text. A serializer that puts JSON numbers
		/// through a double would otherwise hand back different digits than were sent.
		/// </summary>
		[Test]
		public void AVeryLargeUnsignedValueIsCarriedAsText()
		{
			Assert.That(DexcRemoteValue.ToObject(DebugValue.From(ulong.MaxValue)), Is.EqualTo(ulong.MaxValue.ToString()));
		}

		[Test]
		public void ASmallUnsignedValueStaysANumber()
		{
			Assert.That(DexcRemoteValue.ToObject(DebugValue.From(42UL)), Is.EqualTo(42UL));
		}
	}
}
