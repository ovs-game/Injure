// SPDX-License-Identifier: MIT

using System;
using System.Text;

namespace Injure.Coroutines;

internal static class CoroDiagnostics {
	public static string FormatFault(CoroutineUnhandledFaultInfo fault) {
		StringBuilder sb = new();

		const string indent = "   ";
		sb.AppendLine("unhandled coroutine fault:");
		sb.AppendLine($"{indent}handle: {fault.Trace.Handle}");
		if (!string.IsNullOrEmpty(fault.Trace.Name))
			sb.AppendLine($"{indent}name: {fault.Trace.Name}");
		if (!string.IsNullOrEmpty(fault.Trace.ScopeName))
			sb.AppendLine($"{indent}scope: {fault.Trace.ScopeName}");
		sb.AppendLine($"{indent}phase: {fault.Info.LastPhase}");
		sb.AppendLine($"{indent}start tick: {fault.Info.StartTick}");
		sb.AppendLine($"{indent}fault tick: {fault.Info.TerminalTick}");
		if (!string.IsNullOrEmpty(fault.Trace.CurrentWaitDebugDescription))
			sb.AppendLine($"{indent}wait: {fault.Trace.CurrentWaitDebugDescription}");
		else
			sb.AppendLine($"{indent}wait: <no available debug description>");
		if (fault.Trace is not null && fault.Trace.Frames.Count > 0) {
			sb.AppendLine($"{indent}stacktrace:");
			for (int i = fault.Trace.Frames.Count - 1; i >= 0; i--) {
				CoroutineTraceFrame frame = fault.Trace.Frames[i];
				sb.Append($"{indent}{indent}- ");
				sb.Append(!string.IsNullOrEmpty(frame.DebugName) ? frame.DebugName : frame.EnumeratorTypeName);
				if (!string.IsNullOrEmpty(frame.SourceMember))
					sb.Append($" in {frame.SourceMember}");
				else
					sb.Append(" in <no source member info available>");
				if (!string.IsNullOrEmpty(frame.SourceFile))
					sb.Append($" @ {frame.SourceFile}:{frame.SourceLine}");
				else
					sb.Append(" @ <no source file info available>");
				sb.AppendLine();
			}
		} else {
			sb.AppendLine($"{indent} <no stacktrace info available>");
		}
		sb.AppendLine($"{indent}thrown exception:");
		sb.Append(indent).Append(indent).Append(fault.Exception.ToString().ReplaceLineEndings(Environment.NewLine + indent + indent));
		return sb.ToString();
	}
}
