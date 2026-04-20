// SPDX-License-Identifier: MIT

using Microsoft.CodeAnalysis;

namespace Injure.Analyzers.Shared;

internal static class Diagnostics {
	// IJ0001-IJ0099 infrastructure/shared
	// IJ0100-IJ0199 ClosedEnum
	// IJ0200-IJ0299 ClosedFlags
	// IJ0300-IJ0399 ClosedUnion
	// IJ0400-IJ0499 StronglyTypedInt
	// IJ9900-IJ9999 diagnostics for obsoletion/deprecation
	// TODO: also figure out subranges which also probably means renumbering them Again

#pragma warning disable RS2008 // enable analyzer release tracking
	public static readonly DiagnosticDescriptor ClosedEnumInvalidTarget = new DiagnosticDescriptor(
		id: "IJ0101",
		title: "invalid target for attribute ClosedEnum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMustBeReadonly = new DiagnosticDescriptor(
		id: "IJ0102",
		title: "ClosedEnum target must be readonly",
		messageFormat: "ClosedEnum target struct '{0}' must be a readonly struct",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidSourceShape = new DiagnosticDescriptor(
		id: "IJ0103",
		title: "invalid ClosedEnum source shape",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumInvalidCaseEnum = new DiagnosticDescriptor(
		id: "IJ0104",
		title: "invalid ClosedEnum Case enum",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumAliasNotSupported = new DiagnosticDescriptor(
		id: "IJ0105",
		title: "ClosedEnum aliases are not supported",
		messageFormat: "ClosedEnum Case member '{0}' has the same numeric value as '{1}' ({2}); aliases are not supported",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumDefaultRule = new DiagnosticDescriptor(
		id: "IJ0106",
		title: "ClosedEnum default-value rule violation",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumSuspiciousZeroName = new DiagnosticDescriptor(
		id: "IJ0107",
		title: "ClosedEnum member with zero value does not look neutral",
		messageFormat: "ClosedEnum zero-valued member '{0}' does not look like a neutral/default state; consider renaming it or using DefaultIsInvalid = true",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorInvalid = new DiagnosticDescriptor(
		id: "IJ0108",
		title: "invalid ClosedEnum mirror declaration",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor ClosedEnumMirrorMismatch = new DiagnosticDescriptor(
		id: "IJ0109",
		title: "ClosedEnum mirror numeric values do not match",
		messageFormat: "{0}",
		category: "ClosedEnum",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntInvalidTarget = new DiagnosticDescriptor(
		id: "IJ0401",
		title: "invalid target for attribute StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMustBeReadonly = new DiagnosticDescriptor(
		id: "IJ0402",
		title: "StronglyTypedInt target must be readonly",
		messageFormat: "StronglyTypedInt target struct '{0}' must be a readonly struct",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntUnsupportedBacking = new DiagnosticDescriptor(
		id: "IJ0403",
		title: "unsupported backing type for StronglyTypedInt",
		messageFormat: "backing type '{0}' is not supported (supported: int, uint, long, ulong, Int128, UInt128)",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	public static readonly DiagnosticDescriptor StronglyTypedIntMemberCollision = new DiagnosticDescriptor(
		id: "IJ0404",
		title: "existing member collides with reserved member for StronglyTypedInt",
		messageFormat: "{0}",
		category: "StronglyTypedInt",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);
#pragma warning restore RS2008 // enable analyzer release tracking
}
