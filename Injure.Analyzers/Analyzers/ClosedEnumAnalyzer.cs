// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using Injure.Analyzers.Shared;

namespace Injure.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedEnumAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.ClosedEnumInvalidTarget,
		Diagnostics.ClosedEnumMustBeReadonly,
		Diagnostics.ClosedEnumInvalidSourceShape,
		Diagnostics.ClosedEnumInvalidCaseEnum,
		Diagnostics.ClosedEnumAliasNotSupported,
		Diagnostics.ClosedEnumDefaultRule,
		Diagnostics.ClosedEnumSuspiciousZeroName,
		Diagnostics.ClosedEnumMirrorInvalid,
		Diagnostics.ClosedEnumMirrorMismatch
	);

	public override void Initialize(AnalysisContext ctx) {
		ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		ctx.EnableConcurrentExecution();
		ctx.RegisterSymbolAction(analyzeNamedType, SymbolKind.NamedType);
	}

	private static void analyzeNamedType(SymbolAnalysisContext ctx) {
		INamedTypeSymbol sym = (INamedTypeSymbol)ctx.Symbol;
		AttributeData? attr = Util.GetAttribute(sym, AttributeSources.ClosedEnumAttributeMetadataName);
		if (attr is null)
			return;

		Location loc = Util.GetAttributeLocation(attr, sym, ctx.CancellationToken);
		if (sym.TypeKind != TypeKind.Struct) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "Target must be a struct.");
		} else if (!Util.Partial(sym, ctx.CancellationToken)) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "Target must be declared 'partial'.");
		} else if (!sym.IsReadOnly) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedEnumMustBeReadonly, loc, sym.Name));
		} else if (sym.IsRefLikeType) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "Ref structs are not supported.");
		} else if (sym.IsRecord) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "'record struct' is not supported.");
		} else if (sym.ContainingType is not null) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "Nested structs are not supported.");
		} else if (sym.TypeParameters.Length != 0) {
			report(ctx, Diagnostics.ClosedEnumInvalidTarget, loc, "Generic structs are not supported.");
		} else {
			List<EnumDeclarationSyntax> caseDecls = new List<EnumDeclarationSyntax>();
			foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
				if (sr.GetSyntax(ctx.CancellationToken) is not StructDeclarationSyntax decl)
					continue;
				foreach (MemberDeclarationSyntax member in decl.Members) {
					if (member is EnumDeclarationSyntax enumDecl && enumDecl.Identifier.ValueText == "Case") {
						caseDecls.Add(enumDecl);
						continue;
					}
					report(ctx, Diagnostics.ClosedEnumInvalidSourceShape, member.GetLocation(),
						$"ClosedEnum struct '{sym.Name}' must contain no members other than a nested enum named 'Case'.");
				}
			}

			if (caseDecls.Count != 1) {
				report(ctx, Diagnostics.ClosedEnumInvalidSourceShape, loc,
					$"ClosedEnum struct '{sym.Name}' must contain exactly one nested enum named 'Case'.");
				return;
			}

			INamedTypeSymbol? caseSymbol = getNestedEnum(sym, "Case");
			if (caseSymbol is null) {
				report(ctx, Diagnostics.ClosedEnumInvalidSourceShape, caseDecls[0].GetLocation(),
					$"ClosedEnum struct '{sym.Name}' must contain exactly one nested enum named 'Case'.");
				return;
			}
			if (Util.IsFlagsEnum(caseSymbol)) {
				report(ctx, Diagnostics.ClosedEnumInvalidCaseEnum, caseDecls[0].Identifier.GetLocation(),
					"ClosedEnum Case enum must not be marked with [Flags]. Use ClosedFlags for flag sets.");
				return;
			}

			bool defaultIsInvalid = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedEnumAttributeDefaultIsInvalidName, false);
			bool checkZeroNames = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedEnumAttributeCheckZeroNameName, true);
			analyzeCaseEnum(ctx, caseSymbol, defaultIsInvalid, checkZeroNames);
			analyzeMirrors(ctx, sym, caseSymbol);
		}
	}

	private static INamedTypeSymbol? getNestedEnum(INamedTypeSymbol sym, string name) {
		INamedTypeSymbol? result = null;
		foreach (INamedTypeSymbol type in sym.GetTypeMembers(name)) {
			if (type.TypeKind != TypeKind.Enum)
				continue;
			if (result is not null)
				return null;
			result = type;
		}
		return result;
	}

	private static void analyzeCaseEnum(SymbolAnalysisContext ctx, INamedTypeSymbol caseSymbol, bool defaultIsInvalid, bool checkZeroNames) {
		Dictionary<ulong, IFieldSymbol> seenValues = new Dictionary<ulong, IFieldSymbol>();
		List<IFieldSymbol> zeroFields = new List<IFieldSymbol>();
		foreach (IFieldSymbol field in caseSymbol.GetMembers().OfType<IFieldSymbol>()) {
			if (field.IsImplicitlyDeclared || !field.HasConstantValue)
				continue;
			if (Constants.ClosedEnumReservedMemberNames.Contains(field.Name)) {
				report(ctx, Diagnostics.ClosedEnumInvalidCaseEnum, Util.GetLocation(field, caseSymbol),
					$"Case member name '{field.Name}' is reserved by generated ClosedEnum code.");
				continue;
			}
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v)) {
				report(ctx, Diagnostics.ClosedEnumInvalidCaseEnum, Util.GetLocation(field, caseSymbol),
					$"Case member '{field.Name}' does not have a supported constant value.");
				continue;
			}
			if (seenValues.TryGetValue(v, out IFieldSymbol? existing)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedEnumAliasNotSupported, Util.GetLocation(field, caseSymbol),
					field.Name, existing.Name, Util.UInt64Display(v)));
				continue;
			}
			seenValues.Add(v, field);
			if (v == 0)
				zeroFields.Add(field);
		}

		if (defaultIsInvalid) {
			foreach (IFieldSymbol zero in zeroFields)
				report(ctx, Diagnostics.ClosedEnumDefaultRule, Util.GetLocation(zero, caseSymbol),
					"Case enum must not have any members with a value of 0 when DefaultIsInvalid = true.");
		} else {
			if (zeroFields.Count == 0) {
				report(ctx, Diagnostics.ClosedEnumDefaultRule, Util.GetPrimaryLocation(caseSymbol),
					"Case enum must have exactly one member with a value of 0 when DefaultIsInvalid = false.");
			} else if (zeroFields.Count > 1) {
				// technically dead but just in case
				foreach (IFieldSymbol zero in zeroFields)
					report(ctx, Diagnostics.ClosedEnumDefaultRule, Util.GetLocation(zero, caseSymbol),
						"Case enum must have exactly one member with a value of 0 when DefaultIsInvalid = false.");
			} else if (checkZeroNames && !isNeutralZeroName(zeroFields[0].Name)) {
				report(ctx, Diagnostics.ClosedEnumSuspiciousZeroName, Util.GetLocation(zeroFields[0], caseSymbol), zeroFields[0].Name);
			}
		}
	}

	private static void analyzeMirrors(SymbolAnalysisContext ctx, INamedTypeSymbol structSymbol, INamedTypeSymbol caseSymbol) {
		AttributeData[] attrs = Util.GetAttributes(structSymbol, AttributeSources.ClosedEnumMirrorAttributeMetadataName).ToArray();
		if (attrs.Length == 0)
			return;

		HashSet<ulong> closedVals = new HashSet<ulong>();
		foreach (IFieldSymbol field in caseSymbol.GetMembers().OfType<IFieldSymbol>()) {
			if (field.IsImplicitlyDeclared || !field.HasConstantValue || !Util.TryGetEnumMemberUInt64(field, out ulong v))
				continue;
			closedVals.Add(v);
		}

		HashSet<INamedTypeSymbol> seenMirrors = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (AttributeData attr in attrs) {
			bool subset = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedEnumMirrorAttributeSubsetName, false);
			Location loc = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation() ?? Util.GetLocation(structSymbol, structSymbol);
			if (!Util.TryGetMirrorEnum(attr, out INamedTypeSymbol? external) || external is null) {
				report(ctx, Diagnostics.ClosedEnumMirrorInvalid, loc, "ClosedEnumMirror must have exactly one typeof(TEnum) argument.");
				continue;
			}
			if (external.TypeKind != TypeKind.Enum) {
				report(ctx, Diagnostics.ClosedEnumMirrorInvalid, loc, $"ClosedEnumMirror target '{external.ToDisplayString()}' must be an enum.");
				continue;
			}
			if (!seenMirrors.Add(external)) {
				report(ctx, Diagnostics.ClosedEnumMirrorInvalid, loc, $"Duplicate ClosedEnumMirror for '{external.ToDisplayString()}'.");
				continue;
			}
			if (!SymbolEqualityComparer.Default.Equals(caseSymbol.EnumUnderlyingType, external.EnumUnderlyingType)) {
				report(ctx, Diagnostics.ClosedEnumMirrorInvalid, loc,
					$"ClosedEnumMirror target '{external.ToDisplayString()}' must have the same underlying type as '{caseSymbol.ToDisplayString()}'.");
				continue;
			}

			HashSet<ulong> externalVals = new HashSet<ulong>();
			foreach (IFieldSymbol field in external.GetMembers().OfType<IFieldSymbol>()) {
				if (field.IsImplicitlyDeclared || !field.HasConstantValue || !Util.TryGetEnumMemberUInt64(field, out ulong v))
					continue;
				externalVals.Add(v);
			}

			foreach (ulong v in closedVals)
				if (!externalVals.Contains(v))
					report(ctx, Diagnostics.ClosedEnumMirrorMismatch, loc,
						$"ClosedEnumMirror has a numeric value '{Util.UInt64Display(v)}' not present in the target enum ('{external.ToDisplayString()}').");
			if (!subset)
				foreach (ulong v in externalVals)
					if (!closedVals.Contains(v))
						report(ctx, Diagnostics.ClosedEnumMirrorMismatch, loc,
							$"ClosedEnumMirror target enum ('{external.ToDisplayString()}') has a numeric value '{Util.UInt64Display(v)}' not present in '{structSymbol.Name}.Case'.");
		}
	}

	private static bool isNeutralZeroName(string name) {
		if (Constants.ClosedEnumNeutralZeroNames.Contains(name))
			return true;
		foreach (string prefix in Constants.ClosedEnumNeutralZeroPrefixes)
			if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]))
				return true;
		return false;
	}

	private static void report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, Location loc, string msg) =>
		context.ReportDiagnostic(Diagnostic.Create(descriptor, loc, msg));
}
