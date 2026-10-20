using Evenmere.Core.Numerics;

namespace Evenmere.Sim.Tests.Scenarios;

/// <summary>Reads, shows and evaluates the <c>"&lt;op&gt; &lt;quantity&gt;"</c> text of a stock expectation.</summary>
internal static class StockComparison
{
    // Two-character operators come first so ">=" is never read as ">" followed by "= 10".
    private static readonly (string Symbol, ComparisonOp Op)[] _operators =
    [
        (">=", ComparisonOp.GreaterOrEqual),
        ("<=", ComparisonOp.LessOrEqual),
        ("==", ComparisonOp.Equal),
        (">", ComparisonOp.Greater),
        ("<", ComparisonOp.Less),
    ];

    /// <summary>Gets the operators as the file spells them, for messages.</summary>
    public static string Spellings => string.Join(", ", _operators.Select(static o => o.Symbol));

    /// <summary>Reads an expectation such as <c>&gt;= 10</c> or <c>==0.5</c>. Whitespace around the quantity is optional.</summary>
    /// <param name="text">The text from the file.</param>
    /// <param name="op">The operator, when the text is valid.</param>
    /// <param name="quantity">The quantity, when the text is valid.</param>
    /// <param name="error">Why the text is not valid; empty when it is.</param>
    /// <returns><see langword="true"/> if the text is valid.</returns>
    public static bool TryParse(string text, out ComparisonOp op, out Amount quantity, out string error)
    {
        string trimmed = text.Trim();
        foreach ((string symbol, ComparisonOp candidate) in _operators)
        {
            if (!trimmed.StartsWith(symbol, StringComparison.Ordinal))
            {
                continue;
            }

            string number = trimmed[symbol.Length..].Trim();
            try
            {
                quantity = Amount.Parse(number);
                op = candidate;
                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                op = default;
                quantity = default;
                error = $"\"{number}\" is not a quantity: {ex.Message}";
                return false;
            }
        }

        op = default;
        quantity = default;
        error = $"\"{text}\" must start with one of {Spellings}, followed by a quantity such as 10 or 2.5.";
        return false;
    }

    /// <summary>Gets the spelling of an operator.</summary>
    /// <param name="op">The operator.</param>
    /// <returns>The symbol, such as <c>&gt;=</c>.</returns>
    public static string Symbol(ComparisonOp op)
    {
        foreach ((string symbol, ComparisonOp candidate) in _operators)
        {
            if (candidate == op)
            {
                return symbol;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown comparison operator.");
    }

    /// <summary>Tests an actual stock against an expectation.</summary>
    /// <param name="actual">The stock the run ended with.</param>
    /// <param name="op">The operator.</param>
    /// <param name="expected">The quantity from the file.</param>
    /// <returns><see langword="true"/> if the comparison holds.</returns>
    public static bool Holds(Amount actual, ComparisonOp op, Amount expected) => op switch
    {
        ComparisonOp.GreaterOrEqual => actual >= expected,
        ComparisonOp.LessOrEqual => actual <= expected,
        ComparisonOp.Equal => actual == expected,
        ComparisonOp.Greater => actual > expected,
        ComparisonOp.Less => actual < expected,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown comparison operator."),
    };
}
