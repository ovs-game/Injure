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
public sealed class ClosedFlagsAnalyzer : DiagnosticAnalyzer {
	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		Diagnostics.ClosedFlagsInvalidTarget,
		Diagnostics.ClosedFlagsMustBeReadonly,
		Diagnostics.ClosedFlagsInvalidSourceShape,
		Diagnostics.ClosedFlagsInvalidBitsEnum,
		Diagnostics.ClosedFlagsAliasNotSupported,
		Diagnostics.ClosedFlagsBadMemberValue,
		Diagnostics.ClosedFlagsDefaultRule,
		Diagnostics.ClosedFlagsSuspiciousZeroName,
		Diagnostics.ClosedFlagsMirrorInvalid,
		Diagnostics.ClosedFlagsMirrorMismatch
	);

	public override void Initialize(AnalysisContext ctx) {
		ctx.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		ctx.EnableConcurrentExecution();
		ctx.RegisterSymbolAction(analyze, SymbolKind.NamedType);
	}

	private static void analyze(SymbolAnalysisContext ctx) {
		if (ctx.Symbol is not INamedTypeSymbol sym)
			return;
		AttributeData? attr = Util.GetAttribute(sym, AttributeSources.ClosedFlagsAttributeMetadataName);
		if (attr is null)
			return;

		Location loc = Util.GetAttributeLocation(attr, sym, ctx.CancellationToken);
		if (sym.TypeKind != TypeKind.Struct) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "Target must be a struct."));
		} else if (!Util.Partial(sym, ctx.CancellationToken)) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "Target must be declared 'partial'."));
		} else if (!sym.IsReadOnly) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMustBeReadonly, loc, sym.Name));
		} else if (sym.IsRefLikeType) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "Ref structs are not supported."));
		} else if (sym.IsRecord) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "'record struct' is not supported."));
		} else if (sym.ContainingType is not null) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "Nested structs are not supported."));
		} else if (sym.TypeParameters.Length != 0) {
			ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidTarget, loc, "Generic structs are not supported."));
		} else {
			List<EnumDeclarationSyntax> bitsDecls = new List<EnumDeclarationSyntax>();
			foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
				if (sr.GetSyntax(ctx.CancellationToken) is not StructDeclarationSyntax decl)
					continue;
				foreach (MemberDeclarationSyntax member in decl.Members) {
					if (member is EnumDeclarationSyntax enumDecl && enumDecl.Identifier.ValueText == "Bits") {
						bitsDecls.Add(enumDecl);
						continue;
					}
					ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidSourceShape, member.GetLocation(),
						$"ClosedFlags struct '{sym.Name}' must contain no members other than a nested enum named 'Bits'."));
				}
			}

			if (bitsDecls.Count != 1) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidSourceShape, loc,
					$"ClosedFlags struct '{sym.Name}' must contain exactly one nested enum named 'Bits'."));
				return;
			}

			INamedTypeSymbol? bitsSymbol = getNestedEnum(sym, "Bits");
			if (bitsSymbol is null) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidSourceShape, bitsDecls[0].GetLocation(),
					$"ClosedFlags struct '{sym.Name}' must contain exactly one nested enum named 'Bits'."));
				return;
			}
			if (!Util.IsFlagsEnum(bitsSymbol)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidBitsEnum, bitsDecls[0].Identifier.GetLocation(),
					"ClosedFlags Bits enum must be marked with [Flags]."));
				return;
			}

			bool defaultIsInvalid = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedFlagsAttributeDefaultIsInvalidName, false);
			bool checkZeroNames = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedFlagsAttributeCheckZeroNameName, true);
			ulong closedMask = analyzeBitsEnum(ctx, bitsSymbol, defaultIsInvalid, checkZeroNames);
			analyzeMirrors(ctx, sym, bitsSymbol, closedMask);
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

	private static ulong analyzeBitsEnum(SymbolAnalysisContext ctx, INamedTypeSymbol bitsSymbol, bool defaultIsInvalid, bool checkZeroNames) {
		Dictionary<ulong, IFieldSymbol> seenValues = new Dictionary<ulong, IFieldSymbol>();
		List<IFieldSymbol> zeroFields = new List<IFieldSymbol>();
		ulong atomicMask = 0;
		IFieldSymbol[] fields = bitsSymbol.GetMembers().OfType<IFieldSymbol>().Where(static f => f.HasConstantValue).ToArray();
		foreach (IFieldSymbol field in fields) {
			if (Constants.ClosedFlagsReservedMemberNames.Contains(field.Name)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidBitsEnum, Util.GetLocation(field, bitsSymbol),
					$"Case member name '{field.Name}' is reserved by generated ClosedFlags code."));
				continue;
			}
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsInvalidBitsEnum, Util.GetLocation(field, bitsSymbol),
					$"Case member '{field.Name}' does not have a supported constant value."));
				continue;
			}
			if (seenValues.TryGetValue(v, out IFieldSymbol? existing)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsAliasNotSupported, Util.GetLocation(field, bitsSymbol),
					field.Name, existing.Name, Util.UInt64Display(v)));
				continue;
			}
			seenValues.Add(v, field);
			if (v == 0)
				zeroFields.Add(field);
			else if ((v & (v - 1)) == 0)
				atomicMask |= v;
		}

		foreach (IFieldSymbol field in fields) {
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v))
				continue;
			if (v == 0 || (v & (v - 1)) == 0)
				continue;
			if ((v & ~atomicMask) != 0)
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsBadMemberValue, Util.GetLocation(field, bitsSymbol), field.Name));
		}

		if (defaultIsInvalid) {
			foreach (IFieldSymbol zero in zeroFields)
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsDefaultRule, Util.GetLocation(zero, bitsSymbol),
					"Bits enum must not have any members with a value of 0 when DefaultIsInvalid = true."));
		} else {
			if (zeroFields.Count == 0) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsDefaultRule, Util.GetPrimaryLocation(bitsSymbol),
					"Bits enum must have exactly one member with a value of 0 when DefaultIsInvalid = false."));
			} else if (zeroFields.Count > 1) {
				// technically unreachable but just in case
				foreach (IFieldSymbol zero in zeroFields)
					ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsDefaultRule, Util.GetLocation(zero, bitsSymbol),
						"Flags enum must have exactly one member with a value of 0 when DefaultIsInvalid = false."));
			} else if (checkZeroNames && !isNeutralZeroName(zeroFields[0].Name)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsSuspiciousZeroName, Util.GetLocation(zeroFields[0], bitsSymbol), zeroFields[0].Name));
			}
		}
		return atomicMask;
	}

	private static void analyzeMirrors(SymbolAnalysisContext ctx, INamedTypeSymbol structSymbol, INamedTypeSymbol bitsSymbol, ulong closedMask) {
		AttributeData[] attrs = Util.GetAttributes(structSymbol, AttributeSources.ClosedFlagsMirrorAttributeMetadataName).ToArray();
		if (attrs.Length == 0)
			return;

		HashSet<INamedTypeSymbol> seenMirrors = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
		foreach (AttributeData attr in attrs) {
			bool subset = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedFlagsMirrorAttributeSubsetName, false);
			Location loc = attr.ApplicationSyntaxReference?.GetSyntax(ctx.CancellationToken).GetLocation() ?? Util.GetLocation(structSymbol, structSymbol);
			if (!Util.TryGetMirrorEnum(attr, out INamedTypeSymbol? external) || external is null) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorInvalid, loc, "ClosedFlagsMirror must have exactly one typeof(TEnum) argument."));
				continue;
			}
			if (external.TypeKind != TypeKind.Enum) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorInvalid, loc, $"ClosedFlagsMirror target '{external.ToDisplayString()}' must be an enum."));
				continue;
			}
			if (!seenMirrors.Add(external)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorInvalid, loc, $"Duplicate ClosedFlagsMirror for '{external.ToDisplayString()}'."));
				continue;
			}
			if (!SymbolEqualityComparer.Default.Equals(bitsSymbol.EnumUnderlyingType, external.EnumUnderlyingType)) {
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorInvalid, loc,
					$"ClosedFlagsMirror target '{external.ToDisplayString()}' must have the same underlying type as '{bitsSymbol.ToDisplayString()}'."));
				continue;
			}

			ulong externalMask = 0;
			foreach (IFieldSymbol field in external.GetMembers().OfType<IFieldSymbol>()) {
				if (field.IsImplicitlyDeclared || !field.HasConstantValue || !Util.TryGetEnumMemberUInt64(field, out ulong v))
					continue;
				externalMask |= v;
			}

			if ((closedMask & ~externalMask) != 0)
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorMismatch, loc,
					$"ClosedFlagsMirror '{structSymbol.Name}.Bits' has one or more bits not present in the target enum ('{external.ToDisplayString()}')."));
			if (!subset && (externalMask & ~closedMask) != 0)
				ctx.ReportDiagnostic(Diagnostic.Create(Diagnostics.ClosedFlagsMirrorMismatch, loc,
					$"ClosedFlagsMirror target enum ('{external.ToDisplayString()}') has one or more bits not present in '{structSymbol.Name}.Bits'; if this is intentional, consider using 'Subset = true'."));
		}
	}

	private static bool isNeutralZeroName(string name) {
		if (Constants.ClosedTypeNeutralZeroNames.Contains(name))
			return true;
		foreach (string prefix in Constants.ClosedTypeNeutralZeroPrefixes)
			if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]))
				return true;
		return false;
	}
}
