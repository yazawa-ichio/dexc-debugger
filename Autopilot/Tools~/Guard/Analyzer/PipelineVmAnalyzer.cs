using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dexc.Guard
{
	/// <summary>
	/// Turns what the Pipeline interpreter cannot run into compile errors. Each rule names something measured on an IL2CPP
	/// Player and says what to write instead, because the reader of these messages is a coding agent that will act on them.
	/// </summary>
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	public sealed class PipelineVmAnalyzer : DiagnosticAnalyzer
	{
		private static DiagnosticDescriptor Rule(string id, string message) =>
			new DiagnosticDescriptor(id, id, message, "DexcPipelineVm", DiagnosticSeverity.Error, true);

		private static readonly DiagnosticDescriptor Catch = Rule(
			"DEXC001",
			"try/catch cannot run on this VM. Return a result instead; the host stops the machine on an exception. try/finally is fine."
		);
		private static readonly DiagnosticDescriptor Async = Rule(
			"DEXC002",
			"async/await cannot run on this VM. Use a coroutine driven by the host, or a step run each tick."
		);
		private static readonly DiagnosticDescriptor RefParameter = Rule(
			"DEXC003",
			"{0} parameters cannot be declared here. Return a value or a class instead. Passing out to a host method is fine."
		);
		private static readonly DiagnosticDescriptor Generic = Rule(
			"DEXC004",
			"Generic declarations cannot run on this VM. Write the concrete type. Using host generics such as List<T> is fine."
		);
		private static readonly DiagnosticDescriptor StructParameter = Rule(
			"DEXC005",
			"Parameter {0} of host struct type {1} arrives as null. Pass its fields, or wrap it in a class. A lambda parameter is fine."
		);
		private static readonly DiagnosticDescriptor Linq = Rule(
			"DEXC006",
			"LINQ is not bound on this VM. Use a for or foreach loop."
		);
		private static readonly DiagnosticDescriptor Unbox = Rule(
			"DEXC007",
			"Casting object to value type {0} cannot run on this VM. Keep the value typed."
		);

		private static readonly DiagnosticDescriptor EnumEquals = Rule(
			"DEXC008",
			"Equals on host enum {0} gives the wrong answer on this VM. Use == instead."
		);
		private static readonly DiagnosticDescriptor ReadOnlyCollection = Rule(
			"DEXC009",
			"{0} is not bound on this VM. Copy to a List<T> or an array, or reach the concrete collection."
		);
		private static readonly DiagnosticDescriptor IteratorDrive = Rule(
			"DEXC010",
			"An interpreted iterator cannot be driven from interpreted code. Hand it to StartCoroutine, or use a foreach over a List<T>."
		);

		private static readonly DiagnosticDescriptor Unsupported = Rule(
			"DEXC011",
			"{0} cannot run on this VM."
		);
		private static readonly DiagnosticDescriptor HostOnlyType = Rule(
			"DEXC012",
			"{0} cannot be used on this VM. Do this in compiled code and expose the result."
		);
		private static readonly DiagnosticDescriptor Blocking = Rule(
			"DEXC013",
			"{0} blocks the main thread and can hang the game for good. Start the work and check for its result on a later step."
		);
		private static readonly DiagnosticDescriptor TypeTest = Rule(
			"DEXC014",
			"Testing against {0} fails at run time on this VM. Keep the value typed."
		);
		private static readonly DiagnosticDescriptor Iterator = Rule(
			"DEXC015",
			"{0} cannot run on this VM. Write an IEnumerator method for the host to run, or build a List<T>."
		);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
			ImmutableArray.Create(
				Catch, Async, RefParameter, Generic, StructParameter, Linq, Unbox, EnumEquals, ReadOnlyCollection, IteratorDrive,
				Unsupported, HostOnlyType, Blocking, TypeTest, Iterator
			);

		public override void Initialize(AnalysisContext context)
		{
			context.EnableConcurrentExecution();
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.RegisterSyntaxNodeAction(c => Report(c, Catch), SyntaxKind.CatchClause);
			context.RegisterSyntaxNodeAction(c => Report(c, Async), SyntaxKind.AwaitExpression);
			context.RegisterSyntaxNodeAction(c => Report(c, Generic), SyntaxKind.TypeParameterList);
			context.RegisterSyntaxNodeAction(c => Report(c, Linq), SyntaxKind.QueryExpression);
			context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
			context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
			context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
			context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
			context.RegisterSyntaxNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
			context.RegisterSyntaxNodeAction(c => ReportNamed(c, Unsupported, "lock"), SyntaxKind.LockStatement);
			context.RegisterSyntaxNodeAction(c => ReportNamed(c, Unsupported, "checked"), SyntaxKind.CheckedStatement, SyntaxKind.CheckedExpression);
			context.RegisterSyntaxNodeAction(c => ReportNamed(c, Unsupported, "A ref local or ref return"), SyntaxKind.RefType);
			context.RegisterSyntaxNodeAction(c => ReportNamed(c, Unsupported, "stackalloc"), SyntaxKind.StackAllocArrayCreationExpression);
			context.RegisterSyntaxNodeAction(c => ReportNamed(c, HostOnlyType, "typeof"), SyntaxKind.TypeOfExpression);
			context.RegisterSyntaxNodeAction(AnalyzeArrayType, SyntaxKind.ArrayType);
			context.RegisterSyntaxNodeAction(
				AnalyzeTypeUse, SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.PredefinedType
			);
			context.RegisterSyntaxNodeAction(AnalyzeYield, SyntaxKind.YieldReturnStatement);
			context.RegisterSyntaxNodeAction(AnalyzeTypeTest, SyntaxKind.IsExpression, SyntaxKind.AsExpression);
			context.RegisterSyntaxNodeAction(AnalyzeDeclarationPattern, SyntaxKind.DeclarationPattern);
		}

		private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor rule) =>
			context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetLocation()));

		private static void ReportNamed(SyntaxNodeAnalysisContext context, DiagnosticDescriptor rule, string name) =>
			context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetLocation(), name));

		private static void AnalyzeArrayType(SyntaxNodeAnalysisContext context)
		{
			var array = (ArrayTypeSyntax)context.Node;
			if (array.RankSpecifiers.Any(r => r.Rank > 1))
				ReportNamed(context, Unsupported, "A multi-dimensional array");
		}

		private static void AnalyzeTypeUse(SyntaxNodeAnalysisContext context)
		{
			if (!(context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is ITypeSymbol type))
				return;
			if (type.SpecialType == SpecialType.System_Decimal)
				ReportNamed(context, Unsupported, "decimal");
			else if (type.TypeKind == TypeKind.Dynamic)
				ReportNamed(context, Unsupported, "dynamic");
			else if (IsReflectionOrThreading(type))
				ReportNamed(context, HostOnlyType, type.Name);
		}

		private static bool IsReflectionOrThreading(ITypeSymbol type)
		{
			var space = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
			return type.ToDisplayString() == "System.Type"
				|| space == "System.Reflection"
				|| space.StartsWith("System.Reflection.", System.StringComparison.Ordinal)
				|| space == "System.Threading";
		}

		private static void AnalyzeYield(SyntaxNodeAnalysisContext context)
		{
			var owner = context.Node.Ancestors().FirstOrDefault(n =>
				n is LocalFunctionStatementSyntax || n is MethodDeclarationSyntax || n is AccessorDeclarationSyntax
			);
			if (owner is LocalFunctionStatementSyntax)
			{
				ReportNamed(context, Iterator, "yield in a local function");
				return;
			}

			if (owner is MethodDeclarationSyntax method)
			{
				var returned = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
				if (returned?.ToDisplayString() != "System.Collections.IEnumerator")
					ReportNamed(context, Iterator, "An iterator returning " + returned?.Name);
			}
		}

		private static void AnalyzeTypeTest(SyntaxNodeAnalysisContext context)
		{
			var test = (BinaryExpressionSyntax)context.Node;
			ReportTypeTest(context, context.SemanticModel.GetTypeInfo(test.Right, context.CancellationToken).Type);
		}

		private static void AnalyzeDeclarationPattern(SyntaxNodeAnalysisContext context)
		{
			var pattern = (DeclarationPatternSyntax)context.Node;
			ReportTypeTest(context, context.SemanticModel.GetTypeInfo(pattern.Type, context.CancellationToken).Type);
		}

		/// <summary>The interpreter tests only the types bound to it. In System those are the primitives and string.</summary>
		private static void ReportTypeTest(SyntaxNodeAnalysisContext context, ITypeSymbol type)
		{
			if (type == null || type.SpecialType != SpecialType.None)
				return;
			var space = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
			if (space == "System" || space.StartsWith("System.", System.StringComparison.Ordinal))
				context.ReportDiagnostic(Diagnostic.Create(TypeTest, context.Node.GetLocation(), type.Name));
		}

		private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
		{
			var parameter = (ParameterSyntax)context.Node;
			// A lambda's parameters arrive through a delegate, which boxes them.
			var owner = parameter.Parent?.Parent;
			if (!(owner is MethodDeclarationSyntax || owner is LocalFunctionStatementSyntax || owner is ConstructorDeclarationSyntax))
				return;
			var modifier = parameter.Modifiers.FirstOrDefault(m =>
				m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword) || m.IsKind(SyntaxKind.InKeyword)
			);
			if (modifier != default)
			{
				context.ReportDiagnostic(Diagnostic.Create(RefParameter, modifier.GetLocation(), modifier.Text));
				return;
			}

			if (parameter.Type == null)
				return;
			var type = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken).Type;
			if (type != null && IsHostStruct(type, context.Compilation))
				context.ReportDiagnostic(Diagnostic.Create(StructParameter, parameter.GetLocation(), parameter.Identifier.Text, type.Name));
		}

		private static bool IsHostStruct(ITypeSymbol type, Compilation compilation) =>
			type.TypeKind == TypeKind.Struct
			&& type.SpecialType == SpecialType.None
			&& !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
			&& type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

		private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
		{
			var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol as IMethodSymbol;
			if (symbol?.ContainingType?.ToDisplayString() == "System.Linq.Enumerable")
				Report(context, Linq);
			if (symbol == null || !(((InvocationExpressionSyntax)context.Node).Expression is MemberAccessExpressionSyntax access))
				return;
			var receiver = context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type;
			if (symbol.Name == "Equals" && receiver?.TypeKind == TypeKind.Enum && IsHost(receiver, context.Compilation))
				context.ReportDiagnostic(Diagnostic.Create(EnumEquals, context.Node.GetLocation(), receiver.Name));
			if (symbol.Name == "MoveNext" && receiver?.ToDisplayString() == "System.Collections.IEnumerator")
				Report(context, IteratorDrive);
			if ((symbol.Name == "Wait" || symbol.Name == "GetResult") && IsTaskLike(receiver))
				context.ReportDiagnostic(Diagnostic.Create(Blocking, context.Node.GetLocation(), symbol.Name));
		}

		private static bool IsTaskLike(ITypeSymbol type)
		{
			var space = type?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
			return space == "System.Threading.Tasks"
				|| space == "System.Runtime.CompilerServices"
				|| space.StartsWith("Cysharp.Threading.Tasks", System.StringComparison.Ordinal);
		}

		private static bool IsHost(ITypeSymbol type, Compilation compilation) =>
			!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

		private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
		{
			var access = (MemberAccessExpressionSyntax)context.Node;
			var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
			ReportReadOnly(context, symbol?.ContainingType);
			if (symbol?.Name == "Result" && IsTaskLike(symbol.ContainingType))
				context.ReportDiagnostic(Diagnostic.Create(Blocking, context.Node.GetLocation(), "Result"));
		}

		private static void AnalyzeElementAccess(SyntaxNodeAnalysisContext context)
		{
			var access = (ElementAccessExpressionSyntax)context.Node;
			ReportReadOnly(context, context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type as INamedTypeSymbol);
		}

		private static void ReportReadOnly(SyntaxNodeAnalysisContext context, INamedTypeSymbol type)
		{
			var name = type?.OriginalDefinition.ToDisplayString();
			if (name != null && name.StartsWith("System.Collections.Generic.IReadOnly", System.StringComparison.Ordinal))
				context.ReportDiagnostic(Diagnostic.Create(ReadOnlyCollection, context.Node.GetLocation(), type.Name));
		}

		private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
		{
			var cast = (CastExpressionSyntax)context.Node;
			var target = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
			var source = context.SemanticModel.GetTypeInfo(cast.Expression, context.CancellationToken).Type;
			if (target?.IsValueType == true && source?.SpecialType == SpecialType.System_Object)
				context.ReportDiagnostic(Diagnostic.Create(Unbox, cast.GetLocation(), target.Name));
		}
	}
}
