// SPDX

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using Injure.Analyzers.Shared;

namespace Injure.Analyzers.Generators;

[Generator]
public sealed class ClosedFlagsGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, string accessibility, string? ns, bool defaultIsInvalid, ImmutableArray<BitInfo> bits, ImmutableArray<BitInfo> atomicBits, ImmutableArray<MirrorInfo> mirrors) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public string Accessibility { get; } = accessibility;
		public string? Namespace { get; } = ns;
		public bool DefaultIsInvalid { get; } = defaultIsInvalid;
		public ImmutableArray<BitInfo> Bits { get; } = bits;
		public ImmutableArray<BitInfo> AtomicBits { get; } = atomicBits;
		public ImmutableArray<MirrorInfo> Mirrors { get; } = mirrors;
	}

	private sealed class BitInfo(string name, ulong value) {
		public string Name { get; } = name;
		public ulong Value { get; } = value;
	}

	private sealed class MirrorInfo(string typeName) {
		public string TypeName { get; } = typeName;
	}

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		context.RegisterPostInitializationOutput(static (IncrementalGeneratorPostInitializationContext ctx) => {
			ctx.AddSource(AttributeSources.ClosedFlagsAttributeFilename, SourceText.From(AttributeSources.ClosedFlagsAttribute, Encoding.UTF8));
			ctx.AddSource(AttributeSources.ClosedFlagsMirrorAttributeFilename, SourceText.From(AttributeSources.ClosedFlagsMirrorAttribute, Encoding.UTF8));
		});
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			AttributeSources.ClosedFlagsAttributeMetadataName,
			predicate: static (SyntaxNode node, CancellationToken _) => node is StructDeclarationSyntax,
			transform: check
		);
		context.RegisterSourceOutput(targets.Collect(), static (SourceProductionContext ctx, ImmutableArray<TargetInfo?> infos) => {
			HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);
			foreach (TargetInfo? info in infos) {
				if (info is null || !seen.Add(info.Symbol))
					continue;
				ctx.AddSource(getHintName(info), SourceText.From(emit(info), Encoding.UTF8));
			}
		});
	}

	private static string getHintName(TargetInfo info) =>
		(info.Namespace is not null ? info.Namespace + "." + info.Symbol.Name : info.Symbol.Name) + Constants.ClosedFlagsGeneratedSourceSuffix;

	private static TargetInfo? check(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
		INamedTypeSymbol sym = (INamedTypeSymbol)ctx.TargetSymbol;
		if (sym.TypeKind != TypeKind.Struct || !Util.Partial(sym, ct) || !sym.IsReadOnly || sym.IsRecord ||
			sym.IsRefLikeType || sym.ContainingType is not null || sym.TypeParameters.Length != 0)
			return null;

		AttributeData attr = ctx.Attributes[0];
		bool defaultIsInvalid = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedFlagsAttributeDefaultIsInvalidName, false);

		INamedTypeSymbol? bitsSymbol = null;
		foreach (INamedTypeSymbol type in sym.GetTypeMembers("Bits")) {
			if (type.TypeKind != TypeKind.Enum)
				continue;
			if (bitsSymbol is not null)
				return null;
			bitsSymbol = type;
		}
		if (bitsSymbol is null || !Util.IsFlagsEnum(bitsSymbol))
			return null;
		if (!hasOnlyBitsMember(sym, ct))
			return null;

		Dictionary<ulong, string> seenValues = new();
		ImmutableArray<BitInfo>.Builder bits = ImmutableArray.CreateBuilder<BitInfo>();
		ImmutableArray<BitInfo>.Builder atomic = ImmutableArray.CreateBuilder<BitInfo>();
		int zeroCount = 0;
		foreach (IFieldSymbol field in bitsSymbol.GetMembers().OfType<IFieldSymbol>()) {
			if (field.IsImplicitlyDeclared || !field.HasConstantValue)
				continue;
			if (Constants.ClosedFlagsReservedMemberNames.Contains(field.Name))
				return null;
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v))
				return null;
			if (seenValues.ContainsKey(v))
				return null;
			seenValues.Add(v, field.Name);
			BitInfo bi = new(field.Name, v);
			bits.Add(bi);
			if (v == 0)
				zeroCount++;
			else if ((v & (v - 1)) == 0)
				atomic.Add(bi);
		}

		if (!defaultIsInvalid && zeroCount != 1)
			return null;
		if (defaultIsInvalid && zeroCount != 0)
			return null;
		if (bits.Count == 0 || atomic.Count == 0)
			return null;

		ImmutableArray<MirrorInfo>.Builder mirrors = ImmutableArray.CreateBuilder<MirrorInfo>();
		HashSet<string> seenMirrorTypeNames = new(StringComparer.Ordinal);
		foreach (AttributeData mirrorAttr in Util.GetAttributes(sym, AttributeSources.ClosedFlagsMirrorAttributeMetadataName)) {
			if (!Util.TryGetMirrorEnum(mirrorAttr, out INamedTypeSymbol? mirrorEnum) || mirrorEnum is null || mirrorEnum.TypeKind != TypeKind.Enum)
				continue;
			string typeName = mirrorEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (seenMirrorTypeNames.Add(typeName))
				mirrors.Add(new MirrorInfo(typeName));
		}

		return new TargetInfo(sym, accessibility(sym), Util.GetNamespace(sym), defaultIsInvalid, bits.ToImmutable(), atomic.ToImmutable(), mirrors.ToImmutable());
	}

	private static bool hasOnlyBitsMember(INamedTypeSymbol sym, CancellationToken ct) {
		int bitsCount = 0;
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
			if (sr.GetSyntax(ct) is not StructDeclarationSyntax decl)
				continue;
			foreach (MemberDeclarationSyntax member in decl.Members) {
				if (member is EnumDeclarationSyntax enumDecl && enumDecl.Identifier.ValueText == "Bits") {
					bitsCount++;
					continue;
				}
				return false;
			}
		}
		return bitsCount == 1;
	}

	// ==========================================================================
	// source emission
	private static string emit(TargetInfo info) {
		string targetType = escapeIdentifier(info.Symbol.Name);
		StringBuilder sb = new(2048);

		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		sb.AppendLine("#pragma warning disable CS1591");
		sb.AppendLine("using System;");
		if (info.Namespace is not null)
			sb.Append("namespace ").Append(info.Namespace).AppendLine(" {");

		sb.Append('\t').Append(info.Accessibility).Append("readonly partial struct ").Append(targetType).Append(" : global::System.IEquatable<").Append(targetType).AppendLine("> {");
		sb.Append("\t\tprivate readonly Bits ").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(";");

		sb.Append("\t\tprivate ").Append(targetType).AppendLine("(Bits mask) {");
		sb.Append("\t\t\t").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(" = mask;");
		sb.AppendLine("\t\t}");

		foreach (BitInfo b in info.Bits) {
			string name = escapeIdentifier(b.Name);
			sb.Append("\t\t/// <inheritdoc cref=\"Bits.").Append(name).AppendLine("\"/>");
			sb.Append("\t\tpublic static ").Append(targetType).Append(' ').Append(name).Append(" => ");
			if (!info.DefaultIsInvalid && b.Value == 0)
				sb.AppendLine("default;");
			else
				sb.Append("new(Bits.").Append(name).AppendLine(");");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.Append("\t\t/// Gets the declared flags represented by this ").Append(targetType).AppendLine(" value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\t/// <exception cref=\"global::System.InvalidOperationException\">");
		sb.AppendLine("\t\t/// This value is not valid.");
		sb.AppendLine("\t\t/// </exception>");
		sb.AppendLine("\t\t/// <remarks>");
		sb.AppendLine("\t\t/// Never returns an undeclared <see cref=\"Bits\"/> value; either returns a value with its set bits");
		sb.AppendLine("\t\t/// all being declared values or throws.");
		sb.AppendLine("\t\t/// </remarks>");
		sb.Append("\t\tpublic Bits Mask => ").Append(Constants.ClosedFlagsValidateMethodName).Append('(').Append(Constants.ClosedFlagsBackingFieldName).AppendLine(");");
		
		sb.Append("\t\tpublic const Bits ").Append(Constants.ClosedFlagsAllBitsConstName).Append(" = ");
		for (int i = 0; i < info.Bits.Length; i++) {
			if (i != 0)
				sb.Append(" | ");
			sb.Append("Bits.").Append(escapeIdentifier(info.Bits[i].Name));
		}
		sb.AppendLine(";");

		sb.Append("\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static bool ").Append(Constants.ClosedFlagsIsDefinedMethodName).Append("(Bits mask) => (mask & ~").Append(Constants.ClosedFlagsAllBitsConstName).AppendLine(") == 0;");
		sb.Append("\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static Bits ").Append(Constants.ClosedFlagsValidateMethodName).AppendLine("(Bits mask) {");
		if (info.DefaultIsInvalid) {
			sb.AppendLine("\t\t\tif (mask == (Bits)0)");
			sb.Append("\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("default(" + info.Symbol.Name + ") is not a valid " + info.Symbol.Name + " value", true)).AppendLine(");");
		}
		sb.Append("\t\t\tif (!").Append(Constants.ClosedFlagsIsDefinedMethodName).AppendLine("(mask))");
		sb.Append("\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("Invalid " + info.Symbol.Name + " value: ", true)).AppendLine(" + mask.ToString());");
		sb.AppendLine("\t\t\treturn mask;");
		sb.AppendLine("\t\t}");

		sb.Append("\t\tpublic bool HasAny(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) != 0;");
		sb.Append("\t\tpublic bool HasAll(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) == flags.Mask;");
		sb.Append("\t\tpublic bool HasNone(").Append(targetType).AppendLine(" flags) => (Mask & flags.Mask) == 0;");

		sb.Append("\t\tpublic bool Equals(").Append(targetType).Append(" other) => ").Append(Constants.ClosedFlagsBackingFieldName).Append(" == other.").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic override bool Equals(object? obj) => obj is ").Append(targetType).AppendLine(" other && Equals(other);");
		sb.Append("\t\tpublic override int GetHashCode() => ").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(".GetHashCode();");
		sb.Append("\t\tpublic static bool operator ==(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.").Append(Constants.ClosedFlagsBackingFieldName).Append(" == right.").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator !=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.").Append(Constants.ClosedFlagsBackingFieldName).Append(" != right.").Append(Constants.ClosedFlagsBackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator |(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(").Append(Constants.ClosedFlagsValidateMethodName).AppendLine("(left.Mask | right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator &(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(").Append(Constants.ClosedFlagsValidateMethodName).AppendLine("(left.Mask & right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ^(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => new(").Append(Constants.ClosedFlagsValidateMethodName).AppendLine("(left.Mask ^ right.Mask));");
		sb.Append("\t\tpublic static ").Append(targetType).Append(" operator ~(").Append(targetType).Append(" val) => new(").Append(Constants.ClosedFlagsValidateMethodName).Append("((~val.Mask) & ").Append(Constants.ClosedFlagsAllBitsConstName).AppendLine("));");

		foreach (MirrorInfo mirror in info.Mirrors)
			sb.Append("\t\tpublic static explicit operator ").Append(mirror.TypeName).Append('(').Append(targetType).Append(" value) => (").Append(mirror.TypeName).AppendLine(")value.Mask;");

		sb.AppendLine("\t\t/// <summary>");
		sb.AppendLine("\t\t/// Returns the declared mask value for this value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\tpublic override string ToString() => Mask.ToString();");

		emitFlagsHelper(sb, info, targetType);

		sb.AppendLine("\t}");
		if (info.Namespace is not null)
			sb.AppendLine("}");

		return sb.ToString();
	}

	private static void emitFlagsHelper(StringBuilder sb, TargetInfo info, string targetType) {
		sb.AppendLine("\t\tpublic static class Flags {");

		sb.Append("\t\t\tprivate static readonly ").Append(targetType).AppendLine("[] _values = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\t").Append(targetType).Append('.').Append(escapeIdentifier(b.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.Append("\t\t\tpublic static global::System.ReadOnlySpan<").Append(targetType).AppendLine("> Values => _values;");

		sb.AppendLine("\t\t\tprivate static readonly Bits[] _bitValues = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\tBits.").Append(escapeIdentifier(b.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<Bits> BitValues => _bitValues;");

		sb.AppendLine("\t\t\tprivate static readonly string[] _names = new[] {");
		foreach (BitInfo b in info.Bits)
			sb.Append("\t\t\t\t").Append(SymbolDisplay.FormatLiteral(b.Name, true)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<string> Names => _names;");

		sb.Append("\t\t\tpublic static bool IsDefined(Bits mask) => ").Append(Constants.ClosedFlagsIsDefinedMethodName).AppendLine("(mask);");

		sb.Append("\t\t\tpublic static bool TryFromMask(Bits mask, out ").Append(targetType).AppendLine(" val) {");
		sb.Append("\t\t\t\tif (").Append(Constants.ClosedFlagsIsDefinedMethodName).AppendLine("(mask)) {");
		sb.Append("\t\t\t\t\tval = new ").Append(targetType).AppendLine("(mask);");
		sb.AppendLine("\t\t\t\t\treturn true;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tval = default;");
		sb.AppendLine("\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");

		sb.Append("\t\t\tpublic static ").Append(targetType).AppendLine(" FromMask(Bits mask) {");
		sb.Append("\t\t\t\tif (TryFromMask(mask, out ").Append(targetType).AppendLine(" value))");
		sb.AppendLine("\t\t\t\t\treturn value;");
		sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mask), mask, null);");
		sb.AppendLine("\t\t\t}");

		foreach (MirrorInfo mirror in info.Mirrors) {
			sb.Append("\t\t\tpublic static bool TryFromMirror(").Append(mirror.TypeName).Append(" mirror, out ").Append(targetType).AppendLine(" val) => TryFromMask((Bits)mirror, out val);");
			sb.Append("\t\t\tpublic static ").Append(targetType).Append(" FromMirror(").Append(mirror.TypeName).AppendLine(" mirror) {");
			sb.Append("\t\t\t\tif (TryFromMask((Bits)mirror, out ").Append(targetType).AppendLine(" val))");
			sb.AppendLine("\t\t\t\t\treturn val;");
			sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mirror), mirror, null);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t}");
	}

	private static string accessibility(INamedTypeSymbol sym) => sym.DeclaredAccessibility switch {
		Accessibility.Public => "public ",
		Accessibility.Internal => "internal ",
		_ => ""
	};

	private static string escapeIdentifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
		SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
