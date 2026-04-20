// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using Injure.Analyzers.Shared;

namespace Injure.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StronglyTypedIntAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.StronglyTypedIntInvalidTarget,
		Diagnostics.StronglyTypedIntMustBeReadonly,
		Diagnostics.StronglyTypedIntUnsupportedBacking,
		Diagnostics.StronglyTypedIntMemberCollision
	);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
	}

	private static void analyzeNamedType(SymbolAnalysisContext context) {
		INamedTypeSymbol sym = (INamedTypeSymbol)context.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, AttributeSources.StronglyTypedIntAttributeMetadataName);
		if (attr is null)
			return;

		Location loc = Util.GetAttributeLocation(attr, sym, context.CancellationToken);
		if (sym.TypeKind != TypeKind.Struct)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Target must be a struct.");
		else if (!Util.Partial(sym, context.CancellationToken))
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Target must be declared 'partial'.");
		else if (!sym.IsReadOnly)
			context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedIntMustBeReadonly, loc, sym.Name));
		else if (sym.IsRefLikeType)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Ref structs are not supported.");
		else if (sym.IsRecord)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "'record struct' is not supported.");
		else if (sym.ContainingType is not null)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Nested structs are not supported.");
		else if (sym.TypeParameters.Length != 0)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Generic structs are not supported.");
		else if (attr.ConstructorArguments.Length != 1)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Attribute must have exactly one typeof(...) argument.");
		else if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol backingType)
			report(context, Diagnostics.StronglyTypedIntInvalidTarget, loc, "Attribute argument must be a concrete type.");
		else if (!Util.TryGetStronglyTypedIntBackingInfo(backingType, out _, out _))
			context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedIntUnsupportedBacking, loc, backingType.ToDisplayString()));
		else if (Util.CheckStronglyTypedIntCollision(sym, backingType, out Location collisionLoc, out string? collisionMsg))
			context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StronglyTypedIntMemberCollision, collisionLoc, collisionMsg));
	}
	private static void report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, Location loc, string msg) =>
		context.ReportDiagnostic(Diagnostic.Create(descriptor, loc, msg));
}
