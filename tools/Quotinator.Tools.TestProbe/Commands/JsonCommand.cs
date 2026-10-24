using Quotinator.Tools.TestProbe.Json;
using Quotinator.Tools.TestProbe.Results;

namespace Quotinator.Tools.TestProbe.Commands;

/// <summary><c>json</c>: asserts on a JSON document read from stdin.</summary>
internal sealed class JsonCommand : IProbeCommand
{
    /// <inheritdoc/>
    public ExpectationResult Run(IReadOnlyList<string> args, TextReader stdin)
    {
        IReadOnlyList<JsonExpectation> expectations = JsonExpectationParser.Parse(args, out IReadOnlyList<string> errors);

        return errors.Count > 0
            ? new ExpectationResult([], errors)
            : JsonExpectationEvaluator.Evaluate(stdin.ReadToEnd().TrimStart(ByteOrderMark), expectations);
    }

    /// <summary>PowerShell 5.1 prefixes one when it pipes a string into a native process.</summary>
    private const char ByteOrderMark = '﻿';
}
