// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using Injure.Analyzers.Shared;

namespace Injure.Analyzers.Generators;

[Generator]
public sealed class ClosedEnumGenerator : IIncrementalGenerator {
	// ==========================================================================
	// internal types
	private sealed class TargetInfo(INamedTypeSymbol symbol, string accessibility, string? ns, bool defaultIsInvalid, ImmutableArray<CaseInfo> cases, ImmutableArray<MirrorInfo> mirrors) {
		public INamedTypeSymbol Symbol { get; } = symbol;
		public string Accessibility { get; } = accessibility;
		public string? Namespace { get; } = ns;
		public bool DefaultIsInvalid { get; } = defaultIsInvalid;
		public ImmutableArray<CaseInfo> Cases { get; } = cases;
		public ImmutableArray<MirrorInfo> Mirrors { get; } = mirrors;
	}

	private readonly struct CaseInfo(string name, bool isZero) {
		public string Name { get; } = name;
		public bool IsZero { get; } = isZero;
	}

	private sealed class MirrorInfo(string typeName) {
		public string TypeName { get; } = typeName;
	}

	// ==========================================================================
	// IIncrementalGenerator
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		context.RegisterPostInitializationOutput(static (IncrementalGeneratorPostInitializationContext ctx) => {
			ctx.AddSource(AttributeSources.ClosedEnumAttributeFilename, SourceText.From(AttributeSources.ClosedEnumAttribute, Encoding.UTF8));
			ctx.AddSource(AttributeSources.ClosedEnumMirrorAttributeFilename, SourceText.From(AttributeSources.ClosedEnumMirrorAttribute, Encoding.UTF8));
		});
		IncrementalValuesProvider<TargetInfo?> targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			AttributeSources.ClosedEnumAttributeMetadataName,
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
		(info.Namespace is not null ? info.Namespace + "." + info.Symbol.Name : info.Symbol.Name) + Constants.ClosedEnumGeneratedSourceSuffix;

	private static TargetInfo? check(GeneratorAttributeSyntaxContext ctx, CancellationToken ct) {
		INamedTypeSymbol sym = (INamedTypeSymbol)ctx.TargetSymbol;
		if (sym.TypeKind != TypeKind.Struct || !Util.Partial(sym, ct) || !sym.IsReadOnly || sym.IsRecord ||
			sym.IsRefLikeType || sym.ContainingType is not null || sym.TypeParameters.Length != 0)
			return null;

		AttributeData attr = ctx.Attributes[0];
		bool defaultIsInvalid = Util.GetBoolNamedArgument(attr, AttributeSources.ClosedEnumAttributeDefaultIsInvalidName, false);

		INamedTypeSymbol? caseSymbol = null;
		foreach (INamedTypeSymbol type in sym.GetTypeMembers("Case")) {
			if (type.TypeKind != TypeKind.Enum)
				continue;
			if (caseSymbol is not null)
				return null;
			caseSymbol = type;
		}
		if (caseSymbol is null || Util.IsFlagsEnum(caseSymbol))
			return null;
		if (!hasOnlyCaseMember(sym, ct))
			return null;

		Dictionary<ulong, string> seenValues = new();
		ImmutableArray<CaseInfo>.Builder cases = ImmutableArray.CreateBuilder<CaseInfo>();
		int zeroCount = 0;
		foreach (IFieldSymbol field in caseSymbol.GetMembers().OfType<IFieldSymbol>().Where(static f => f.HasConstantValue)) {
			if (Constants.ClosedEnumReservedMemberNames.Contains(field.Name))
				return null;
			if (!Util.TryGetEnumMemberUInt64(field, out ulong v))
				return null;
			if (seenValues.ContainsKey(v))
				return null;
			seenValues.Add(v, field.Name);
			if (v == 0)
				zeroCount++;
			cases.Add(new CaseInfo(field.Name, v == 0));
		}

		if (!defaultIsInvalid && zeroCount != 1)
			return null;
		if (defaultIsInvalid && zeroCount != 0)
			return null;
		if (cases.Count == 0)
			return null;

		ImmutableArray<MirrorInfo>.Builder mirrors = ImmutableArray.CreateBuilder<MirrorInfo>();
		HashSet<string> seenMirrorTypeNames = new(StringComparer.Ordinal);
		foreach (AttributeData mirrorAttr in Util.GetAttributes(sym, AttributeSources.ClosedEnumMirrorAttributeMetadataName)) {
			if (!Util.TryGetMirrorEnum(mirrorAttr, out INamedTypeSymbol? mirrorEnum) || mirrorEnum is null || mirrorEnum.TypeKind != TypeKind.Enum)
				continue;
			string typeName = mirrorEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (seenMirrorTypeNames.Add(typeName))
				mirrors.Add(new MirrorInfo(typeName));
		}

		return new TargetInfo(sym, accessibility(sym), Util.GetNamespace(sym), defaultIsInvalid, cases.ToImmutable(), mirrors.ToImmutable());
	}

	private static bool hasOnlyCaseMember(INamedTypeSymbol sym, CancellationToken ct) {
		int caseCount = 0;
		foreach (SyntaxReference sr in sym.DeclaringSyntaxReferences) {
			if (sr.GetSyntax(ct) is not StructDeclarationSyntax decl)
				continue;
			foreach (MemberDeclarationSyntax member in decl.Members) {
				if (member is EnumDeclarationSyntax enumDecl && enumDecl.Identifier.ValueText == "Case") {
					caseCount++;
					continue;
				}
				return false;
			}
		}
		return caseCount == 1;
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

		sb.Append('\t').Append(info.Accessibility).Append(" readonly partial struct ").Append(targetType).Append(" : global::System.IEquatable<").Append(targetType).AppendLine("> {");
		sb.Append("\t\tprivate readonly Case ").Append(Constants.ClosedEnumBackingFieldName).AppendLine(";");

		sb.Append("\t\tprivate ").Append(targetType).AppendLine("(Case tag) {");
		sb.Append("\t\t\t").Append(Constants.ClosedEnumBackingFieldName).AppendLine(" = tag;");
		sb.AppendLine("\t\t}");

		foreach (CaseInfo c in info.Cases) {
			string name = escapeIdentifier(c.Name);
			sb.Append("\t\t/// <inheritdoc cref=\"Case.").Append(name).AppendLine("\"/>");
			sb.Append("\t\tpublic static ").Append(targetType).Append(' ').Append(name).Append(" => ");
			if (!info.DefaultIsInvalid && c.IsZero)
				sb.AppendLine("default;");
			else
				sb.Append("new(Case.").Append(name).AppendLine(");");
		}

		sb.AppendLine("\t\t/// <summary>");
		sb.Append("\t\t/// Gets the declared case represented by this ").Append(targetType).AppendLine(" value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\t/// <exception cref=\"global::System.InvalidOperationException\">");
		sb.AppendLine("\t\t/// This value is not valid.");
		sb.AppendLine("\t\t/// </exception>");
		sb.AppendLine("\t\t/// <remarks>");
		sb.AppendLine("\t\t/// Never returns an undeclared <see cref=\"Case\"/> value; either returns a declared case or throws.");
		sb.AppendLine("\t\t/// As such, the fallback/default arm of an exhaustive switch is unreachable control flow.");
		sb.AppendLine("\t\t/// </remarks>");
		sb.AppendLine("\t\tpublic Case Tag {");
		sb.AppendLine("\t\t\tget {");
		if (info.DefaultIsInvalid) {
			sb.Append("\t\t\t\tif (").Append(Constants.ClosedEnumBackingFieldName).AppendLine(" == (Case)0)");
			sb.Append("\t\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("default(" + info.Symbol.Name + ") is not a valid " + info.Symbol.Name + " value", true)).AppendLine(");");
		}
		sb.Append("\t\t\t\tif (!").Append(Constants.ClosedEnumIsDefinedMethodName).Append('(').Append(Constants.ClosedEnumBackingFieldName).AppendLine("))");
		sb.Append("\t\t\t\t\tthrow new global::System.InvalidOperationException(").Append(SymbolDisplay.FormatLiteral("Invalid " + info.Symbol.Name + " value: ", true)).Append(" + ").Append(Constants.ClosedEnumBackingFieldName).AppendLine(".ToString());");
		sb.Append("\t\t\t\treturn ").Append(Constants.ClosedEnumBackingFieldName).AppendLine(";");
		sb.AppendLine("\t\t\t}");
		sb.AppendLine("\t\t}");

		sb.Append("\t\t[global::System.Runtime.CompilerServices.MethodImplAttribute(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] private static bool ").Append(Constants.ClosedEnumIsDefinedMethodName).Append("(Case tag) => tag is ");
		for (int i = 0; i < info.Cases.Length; i++) {
			if (i != 0)
				sb.Append(" or ");
			sb.Append("Case.").Append(escapeIdentifier(info.Cases[i].Name));
		}
		sb.AppendLine(";");

		sb.Append("\t\tpublic bool Equals(").Append(targetType).Append(" other) => ").Append(Constants.ClosedEnumBackingFieldName).Append(" == other.").Append(Constants.ClosedEnumBackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic override bool Equals(object? obj) => obj is ").Append(targetType).AppendLine(" other && Equals(other);");
		sb.Append("\t\tpublic override int GetHashCode() => ").Append(Constants.ClosedEnumBackingFieldName).AppendLine(".GetHashCode();");
		sb.Append("\t\tpublic static bool operator ==(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.").Append(Constants.ClosedEnumBackingFieldName).Append(" == right.").Append(Constants.ClosedEnumBackingFieldName).AppendLine(";");
		sb.Append("\t\tpublic static bool operator !=(").Append(targetType).Append(" left, ").Append(targetType).Append(" right) => left.").Append(Constants.ClosedEnumBackingFieldName).Append(" != right.").Append(Constants.ClosedEnumBackingFieldName).AppendLine(";");

		foreach (MirrorInfo mirror in info.Mirrors)
			sb.Append("\t\tpublic static explicit operator ").Append(mirror.TypeName).Append('(').Append(targetType).Append(" value) => (").Append(mirror.TypeName).AppendLine(")value.Tag;");

		sb.AppendLine("\t\t/// <summary>");
		sb.AppendLine("\t\t/// Returns the declared case name for this value.");
		sb.AppendLine("\t\t/// </summary>");
		sb.AppendLine("\t\tpublic override string ToString() => Tag.ToString();");

		emitEnumHelper(sb, info, targetType);

		sb.AppendLine("\t}");
		if (info.Namespace is not null)
			sb.AppendLine("}");

		return sb.ToString();
	}

	private static void emitEnumHelper(StringBuilder sb, TargetInfo info, string targetType) {
		sb.AppendLine("\t\tpublic static class Enum {");

		sb.Append("\t\t\tprivate static readonly ").Append(targetType).AppendLine("[] _values = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\t").Append(targetType).Append('.').Append(escapeIdentifier(c.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.Append("\t\t\tpublic static global::System.ReadOnlySpan<").Append(targetType).AppendLine("> Values => _values;");

		sb.AppendLine("\t\t\tprivate static readonly Case[] _tags = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\tCase.").Append(escapeIdentifier(c.Name)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<Case> Tags => _tags;");

		sb.AppendLine("\t\t\tprivate static readonly string[] _names = new[] {");
		foreach (CaseInfo c in info.Cases)
			sb.Append("\t\t\t\t").Append(SymbolDisplay.FormatLiteral(c.Name, true)).AppendLine(",");
		sb.AppendLine("\t\t\t};");
		sb.AppendLine("\t\t\tpublic static global::System.ReadOnlySpan<string> Names => _names;");

		sb.Append("\t\t\tpublic static bool IsDefined(Case tag) => ").Append(Constants.ClosedEnumIsDefinedMethodName).AppendLine("(tag);");

		sb.Append("\t\t\tpublic static bool TryFromTag(Case tag, out ").Append(targetType).AppendLine(" val) {");
		sb.Append("\t\t\t\tif (").Append(Constants.ClosedEnumIsDefinedMethodName).AppendLine("(tag)) {");
		sb.Append("\t\t\t\t\tval = new ").Append(targetType).AppendLine("(tag);");
		sb.AppendLine("\t\t\t\t\treturn true;");
		sb.AppendLine("\t\t\t\t}");
		sb.AppendLine("\t\t\t\tval = default;");
		sb.AppendLine("\t\t\t\treturn false;");
		sb.AppendLine("\t\t\t}");

		sb.Append("\t\t\tpublic static ").Append(targetType).AppendLine(" FromTag(Case tag) {");
		sb.Append("\t\t\t\tif (TryFromTag(tag, out ").Append(targetType).AppendLine(" val))");
		sb.AppendLine("\t\t\t\t\treturn val;");
		sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(tag), tag, null);");
		sb.AppendLine("\t\t\t}");

		foreach (MirrorInfo mirror in info.Mirrors) {
			sb.Append("\t\t\tpublic static bool TryFromMirror(").Append(mirror.TypeName).Append(" mirror, out ").Append(targetType).AppendLine(" val) => TryFromTag((Case)mirror, out val);");
			sb.Append("\t\t\tpublic static ").Append(targetType).Append(" FromMirror(").Append(mirror.TypeName).AppendLine(" mirror) {");
			sb.Append("\t\t\t\tif (TryFromTag((Case)mirror, out ").Append(targetType).AppendLine(" val))");
			sb.AppendLine("\t\t\t\t\treturn val;");
			sb.AppendLine("\t\t\t\tthrow new global::System.ArgumentOutOfRangeException(nameof(mirror), mirror, null);");
			sb.AppendLine("\t\t\t}");
		}

		sb.AppendLine("\t\t}");
	}

	private static string accessibility(INamedTypeSymbol sym) => sym.DeclaredAccessibility switch {
		Accessibility.Public => "public",
		Accessibility.Internal => "internal",
		_ => "",
	};

	private static string escapeIdentifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
		SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
