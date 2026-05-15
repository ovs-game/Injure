// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MonoMod.Cil;

using Injure.ModKit.Abstractions.MonoMod;

namespace Injure.ModKit.MonoMod;

public sealed class HookValidationException(string message) : InvalidOperationException(message) {
}

internal static class HookMethodValidator {
	private readonly record struct ParamSpec(Type Type, bool IsOut) {
		public override string ToString() {
			string prefix = IsOut ? "out " : Type.IsByRef ? "ref " : "";
			Type type = Type.IsByRef ? Type.GetElementType()! : Type;
			return prefix + formatType(type);
		}
	}

	public static void ValidateGeneratedHookMethod(MethodInfo patchMethod, HookTarget target) {
		validatePatchMethodCommon(patchMethod, "hook");

		MethodInfo origInvoke = getDelegateInvoke(target.OrigDelegateType);
		validateHookReplacementAgainstOrigDelegate(patchMethod, target.OrigDelegateType, origInvoke, target.ID);
	}

	public static void ValidateGeneratedILHookMethod(MethodInfo patchMethod, HookTarget target) {
		validateILHookMethod(patchMethod, target.ID);
	}

	public static void ValidateDirectHookMethod(MethodInfo patchMethod, MethodBase targetMethod) {
		validatePatchMethodCommon(patchMethod, "direct hook");

		ParameterInfo[] patchParams = patchMethod.GetParameters();
		if (patchParams.Length == 0)
			throw new HookValidationException($"direct hook method '{formatMethod(patchMethod)}' must take an orig delegate as its first parameter");

		Type origDelegateType = patchParams[0].ParameterType;

		if (!typeof(Delegate).IsAssignableFrom(origDelegateType))
			throw new HookValidationException($"first parameter of direct hook method '{formatMethod(patchMethod)}' must be a delegate type (found '{formatType(origDelegateType)}' instead)");

		MethodInfo origInvoke = getDelegateInvoke(origDelegateType);

		validateOrigDelegateAgainstTarget(origDelegateType, origInvoke, targetMethod, formatMethod(targetMethod));
		validateHookReplacementAgainstOrigDelegate(patchMethod, origDelegateType, origInvoke, formatMethod(targetMethod));
	}

	public static void ValidateDirectILHookMethod(MethodInfo patchMethod, MethodBase targetMethod) {
		validateILHookMethod(patchMethod, formatMethod(targetMethod));
	}

	private static void validatePatchMethodCommon(MethodInfo method, string role) {
		if (!method.IsStatic)
			throw new HookValidationException($"{role} method '{formatMethod(method)}' must be static");
		if (method.ContainsGenericParameters)
			throw new HookValidationException($"{role} method '{formatMethod(method)}' must not have unbound generic parameters");
		if (method.IsGenericMethodDefinition)
			throw new HookValidationException($"{role} method '{formatMethod(method)}' must not be an open generic method");
		if (method.ReturnParameter.ParameterType.IsByRef)
			throw new HookValidationException($"{role} method '{formatMethod(method)}' uses a byref return; this is not supported by the validator yet, sorry");
	}

	private static void validateILHookMethod(MethodInfo method, string targetDescription) {
		validatePatchMethodCommon(method, "IL hook");

		if (method.ReturnType != typeof(void))
			throw new HookValidationException($"IL hook method '{formatMethod(method)}' for target '{targetDescription}' must return void");

		ParameterInfo[] @params = method.GetParameters();
		if (@params.Length != 1)
			throw new HookValidationException($"IL hook method '{formatMethod(method)}' for target '{targetDescription}' must take exactly one parameter of type '{typeof(ILContext).FullName}'");
		if (@params[0].ParameterType != typeof(ILContext))
			throw new HookValidationException($"IL hook method '{formatMethod(method)}' for target '{targetDescription}' must take in '{typeof(ILContext).FullName}' (found '{formatType(@params[0].ParameterType)}' instead)");
	}

	private static void validateHookReplacementAgainstOrigDelegate(MethodInfo patchMethod, Type origDelegateType, MethodInfo origInvoke, string targetDescription) {
		ParameterInfo[] patchParams = patchMethod.GetParameters();
		ParameterInfo[] origParams = origInvoke.GetParameters();

		if (patchParams.Length != origParams.Length + 1)
			throw new HookValidationException(
				$"hook method '{formatMethod(patchMethod)}' for target '{targetDescription}' must take {origParams.Length + 1} parameter(s), them being " +
				$"the orig delegate '{formatType(origDelegateType)}' followed by the original call parameters (found {patchParams.Length} parameter(s) instead)"
			);

		if (patchParams[0].ParameterType != origDelegateType)
			throw new HookValidationException(
				$"parameter 0 of hook method '{formatMethod(patchMethod)}' for target '{targetDescription}' must be orig delegate type " +
				$"'{formatType(origDelegateType)}' (found '{formatType(patchParams[0].ParameterType)}' instead)"
			);

		for (int i = 0; i < origParams.Length; i++) {
			ParameterInfo expected = origParams[i];
			ParameterInfo actual = patchParams[i + 1];
			if (!sameParameterType(actual, expected))
				throw new HookValidationException(
					$"parameter {i + 1} of hook method '{formatMethod(patchMethod)}' for target '{targetDescription}' must be " +
					$"'{formatParameter(expected)}' (found '{formatParameter(actual)}' instead)"
				);
		}

		if (patchMethod.ReturnType != origInvoke.ReturnType)
			throw new HookValidationException(
				$"hook method '{formatMethod(patchMethod)}' for target '{targetDescription}' must return " +
				$"'{formatType(origInvoke.ReturnType)}' (found '{formatType(patchMethod.ReturnType)}' instead)"
			);
	}

	private static void validateOrigDelegateAgainstTarget(Type origDelegateType, MethodInfo origInvoke, MethodBase targetMethod, string targetDescription) {
		if (targetMethod.ContainsGenericParameters)
			throw new HookValidationException($"direct hook target '{targetDescription}' must not have unbound generic parameters");
		if (targetMethod is ConstructorInfo ctor && ctor.IsStatic)
			throw new HookValidationException($"direct hook target '{targetDescription}' cannot be a static constructor");

		Type expectedReturn = getTargetReturnType(targetMethod);
		ParamSpec[] expectedParams = getExpectedOrigDelegateParameters(targetMethod);
		if (origInvoke.ReturnType != expectedReturn)
			throw new HookValidationException(
				$"orig delegate '{formatType(origDelegateType)}' for direct hook target '{targetDescription}' must return " +
				$"'{formatType(expectedReturn)}' (found '{formatType(origInvoke.ReturnType)}' instead)"
			);

		ParameterInfo[] actualParams = origInvoke.GetParameters();
		if (actualParams.Length != expectedParams.Length)
			throw new HookValidationException(
				$"orig delegate '{formatType(origDelegateType)}' for direct hook target '{targetDescription}' must have " +
				$"{expectedParams.Length} parameter(s) (found {actualParams.Length} parameter(s) instead)"
			);

		for (int i = 0; i < expectedParams.Length; i++) {
			ParamSpec expected = expectedParams[i];
			ParameterInfo actual = actualParams[i];
			if (actual.ParameterType != expected.Type || actual.IsOut != expected.IsOut)
				throw new HookValidationException(
					$"parameter {i} of orig delegate '{formatType(origDelegateType)}' for direct hook target '{targetDescription}' must be " +
					$"'{expected}' (found '{formatParameter(actual)}' instead)"
				);
		}
	}

	private static MethodInfo getDelegateInvoke(Type delegateType) {
		if (!typeof(Delegate).IsAssignableFrom(delegateType))
			throw new HookValidationException($"type '{formatType(delegateType)}' is not a delegate type");
		MethodInfo invoke = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance) ??
			throw new HookValidationException($"delegate type '{formatType(delegateType)}' unexpectedly does not expose an Invoke method");
		if (invoke.ContainsGenericParameters)
			throw new HookValidationException($"delegate type '{formatType(delegateType)}' has unbound generic parameters");
		return invoke;
	}

	private static Type getTargetReturnType(MethodBase targetMethod) {
		return targetMethod switch {
			MethodInfo method => method.ReturnType,
			ConstructorInfo => typeof(void),
			_ => throw new HookValidationException($"unsupported hook target type '{targetMethod.GetType().FullName}'"),
		};
	}

	private static ParamSpec[] getExpectedOrigDelegateParameters(MethodBase targetMethod) {
		List<ParamSpec> result = new();
		if (!targetMethod.IsStatic) {
			Type declaringType = targetMethod.DeclaringType ??
				throw new HookValidationException($"instance hook target '{formatMethod(targetMethod)}' has no declaring type");
			result.Add(new ParamSpec(declaringType, IsOut: false));
		}

		foreach (ParameterInfo parameter in targetMethod.GetParameters())
			result.Add(new ParamSpec(parameter.ParameterType, parameter.IsOut));

		return result.ToArray();
	}

	private static bool sameParameterType(ParameterInfo actual, ParameterInfo expected) =>
		actual.ParameterType == expected.ParameterType && actual.IsOut == expected.IsOut;

	private static string formatMethod(MethodBase method) {
		string declaring = method.DeclaringType?.FullName ?? "<unknown>";
		string parameters = string.Join(", ", method.GetParameters().Select(static p => formatType(p.ParameterType)));
		return $"{declaring}.{method.Name}({parameters})";
	}

	private static string formatType(Type type) {
		if (type.IsByRef)
			return formatType(type.GetElementType()!) + "&";
		return type.FullName ?? type.Name;
	}

	private static string formatParameter(ParameterInfo parameter) {
		string prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
		Type type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
		return prefix + formatType(type);
	}
}
