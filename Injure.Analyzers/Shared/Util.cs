// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Injure.Analyzers.Shared;

internal static class Util {
	public static bool HasAttribute(INamedTypeSymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				return true;
		return false;
	}

	public static bool HasAttribute(ISymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				return true;
		return false;
	}

	public static bool Partial(INamedTypeSymbol sym, CancellationToken ct) {
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences)
			if (sr.GetSyntax(ct) is TypeDeclarationSyntax s && s.Modifiers.Any(SyntaxKind.PartialKeyword))
				return true;
		return false;
	}

	public static string? GetNamespace(INamedTypeSymbol sym) =>
		!sym.ContainingNamespace.IsGlobalNamespace ? sym.ContainingNamespace.ToDisplayString() : null;

	public static Location GetLocation(ISymbol sym, INamedTypeSymbol fallback) =>
		sym.Locations.FirstOrDefault(static l => l.IsInSource) ??
		fallback.Locations.FirstOrDefault(static l => l.IsInSource) ??
		Location.None;

	public static Location GetPrimaryLocation(INamedTypeSymbol sym) =>
		sym.Locations.FirstOrDefault(static l => l.IsInSource) ?? Location.None;

	public static AttributeData? GetAttribute(INamedTypeSymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				return attr;
		return null;
	}

	public static IEnumerable<AttributeData> GetAttributes(INamedTypeSymbol sym, string metadataName) {
		foreach (AttributeData attr in sym.GetAttributes())
			if (metadataNameMatches(attr.AttributeClass, metadataName))
				yield return attr;
	}

	private static bool metadataNameMatches(INamedTypeSymbol? sym, string metadataName) {
		if (sym is null)
			return false;
		return sym.ToDisplayString() == metadataName ||
			sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + metadataName;
	}

	public static Location GetAttributeLocation(AttributeData attr, INamedTypeSymbol fallback, CancellationToken ct) =>
		attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? GetPrimaryLocation(fallback);

	public static bool GetBoolNamedArgument(AttributeData attr, string name, bool defaultVal) {
		foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
			if (kv.Key == name && kv.Value.Value is bool b)
				return b;
		return defaultVal;
	}

	public static bool IsFlagsEnum(INamedTypeSymbol enumSymbol) {
		foreach (AttributeData attr in enumSymbol.GetAttributes()) {
			INamedTypeSymbol? cls = attr.AttributeClass;
			if (cls is null)
				continue;
			if (cls.ToDisplayString() == "System.FlagsAttribute" || cls.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.FlagsAttribute")
				return true;
		}
		return false;
	}

	public static bool TryGetEnumMemberUInt64(IFieldSymbol field, out ulong value) {
		switch (field.ConstantValue) {
		case byte x:
			value = x;
			return true;
		case sbyte x:
			value = unchecked((ulong)x);
			return true;
		case short x:
			value = unchecked((ulong)x);
			return true;
		case ushort x:
			value = x;
			return true;
		case int x:
			value = unchecked((ulong)x);
			return true;
		case uint x:
			value = x;
			return true;
		case long x:
			value = unchecked((ulong)x);
			return true;
		case ulong x:
			value = x;
			return true;
		default:
			value = 0;
			return false;
		}
	}

	public static string UInt64Display(ulong value) => value.ToString(CultureInfo.InvariantCulture);

	public static bool TryGetMirrorEnum(AttributeData attr, out INamedTypeSymbol? enumType) {
		enumType = null;
		if (attr.ConstructorArguments.Length != 1)
			return false;

		TypedConstant arg = attr.ConstructorArguments[0];
		if (arg.Kind != TypedConstantKind.Type)
			return false;
		if (arg.Value is not INamedTypeSymbol type)
			return false;

		enumType = type;
		return true;
	}

	public static bool TryGetStronglyTypedIntBackingInfo(INamedTypeSymbol sym, out string? name, out bool signed) {
		switch (sym.SpecialType) {
		case SpecialType.System_Int32:
			name = "int";
			signed = true;
			return true;
		case SpecialType.System_UInt32:
			name = "uint";
			signed = false;
			return true;
		case SpecialType.System_Int64:
			name = "long";
			signed = true;
			return true;
		case SpecialType.System_UInt64:
			name = "ulong";
			signed = false;
			return true;
		}
		if (sym.ContainingNamespace.ToDisplayString() == "System") {
			switch (sym.Name) {
			case "Int128":
				name = "global::System.Int128";
				signed = true;
				return true;
			case "UInt128":
				name = "global::System.UInt128";
				signed = false;
				return true;
			}
		}

		name = null;
		signed = false;
		return false;
	}

	public static bool CheckStronglyTypedIntCollision(INamedTypeSymbol sym, INamedTypeSymbol backingType, out Location loc, out string? msg) {
		ImmutableArray<ISymbol> members = sym.GetMembers(Constants.StronglyTypedIntBackingFieldName);
		if (members.Length != 0) {
			loc = GetLocation(members[0], sym);
			msg = $"Type '{sym.Name}' already contains a member named '{Constants.StronglyTypedIntBackingFieldName}', which is reserved by StronglyTypedInt.";
			return true;
		}
		foreach (IMethodSymbol ctor in sym.InstanceConstructors) {
			if (ctor.IsImplicitlyDeclared || ctor.Parameters.Length != 1 ||
				!SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, backingType))
				continue;
			loc = GetLocation(ctor, sym);
			msg = $"Type '{sym.Name}' already contains a constructor with signature '({backingType.ToDisplayString()})', which conflicts with generated code.";
			return true;
		}
		loc = GetPrimaryLocation(sym);
		msg = null;
		return false;
	}
}
