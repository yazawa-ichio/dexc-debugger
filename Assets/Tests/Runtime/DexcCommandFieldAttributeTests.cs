#if DEXC_DEBUGGER
using System.Linq;
using Dexc;
using NUnit.Framework;
using DebuggerRuntime = Dexc.DexcDebugger;

namespace DexcDebugger.Tests
{
	public sealed class DexcCommandFieldAttributeTests
	{
		[SetUp]
		public void SetUp()
		{
			DebuggerRuntime.Initialize();
		}

		[TearDown]
		public void TearDown()
		{
			DebuggerRuntime.ResetForTests();
		}

		[Test]
		public void RangeSliderAndMultilineReachTheFieldDescriptor()
		{
			var descriptor = Descriptor("Tests/ZZFieldAttributes/Ranged");
			var speed = descriptor.Fields.Single(field => field.Name == "Speed");
			Assert.That(speed.HasRange, Is.True);
			Assert.That(speed.RangeMinimum, Is.EqualTo(0d));
			Assert.That(speed.RangeMaximum, Is.EqualTo(10d));
			Assert.That(speed.IsSlider, Is.True);
			var note = descriptor.Fields.Single(field => field.Name == "Note");
			Assert.That(note.IsMultiline, Is.True);
			Assert.That(note.HasRange, Is.False);
		}

		[Test]
		public void ValuesOutsideTheRangeAreRejected()
		{
			var descriptor = Descriptor("Tests/ZZFieldAttributes/Ranged");
			var speed = descriptor.Fields.Single(field => field.Name == "Speed").Id;
			Assert.That(DebugCommand.Draft.Create(descriptor.Id).TryGetDraftId(out var draft), Is.True);
			Assert.That(
				DebugCommand.Draft.UpdateInput(draft, speed, DebugValue.From(11f)).Status,
				Is.Not.EqualTo(DebugCommandDraftOperationStatus.Succeeded)
			);
			Assert.That(
				DebugCommand.Draft.UpdateInput(draft, speed, DebugValue.From(10f)).Status,
				Is.EqualTo(DebugCommandDraftOperationStatus.Succeeded)
			);
		}

		[Test]
		public void AttributesOnTheWrongInputExcludeTheCommand()
		{
			var items = DebugCommand.Registry.GetList(new DebugListQuery("Tests/ZZFieldAttributes/Wrong", "", new DebugPageRequest(0, 8)));
			Assert.That(items.Items, Is.Empty);
		}

		static DebugCommandDescriptor Descriptor(string path)
		{
			var item = DebugCommand.Registry.GetList(new DebugListQuery(path, "", new DebugPageRequest(0, 8))).Items.Single();
			Assert.That(DebugCommand.Registry.TryGetDescriptor(item.Id, out var descriptor), Is.True);
			return descriptor;
		}
	}

	[DebugCommand("Tests/ZZFieldAttributes/Ranged", Access = DebugCommandAccess.ReadOnly)]
	public sealed class FieldAttributeRangedCommand : DebugCommand
	{
		[DebugRange(0, 10)]
		[DebugSlider]
		public InputFloat Speed = new InputFloat(5f);

		[DebugMultiline]
		public InputString Note = new InputString("");

		protected override void Execute() { }
	}

	[DebugCommand("Tests/ZZFieldAttributes/Wrong", Access = DebugCommandAccess.ReadOnly)]
	public sealed class FieldAttributeWrongCommand : DebugCommand
	{
		[DebugMultiline]
		public InputInt Count = new InputInt(1);

		protected override void Execute() { }
	}
}
#endif
