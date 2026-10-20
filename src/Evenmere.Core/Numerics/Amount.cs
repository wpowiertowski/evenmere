namespace Evenmere.Core.Numerics;

/// <summary>
/// A quantity of goods, coins or points held as an <see cref="long"/> count of milli-units (1 unit = 1000),
/// so the simulation needs no floating point (ADR-0003). All arithmetic is checked and throws
/// <see cref="OverflowException"/> instead of wrapping, and every division rounds toward negative infinity.
/// </summary>
public readonly record struct Amount : IComparable<Amount>
{
    private const long MilliPerUnit = 1000;
    private const int MaxDecimals = 3;

    private Amount(long milli)
    {
        Milli = milli;
    }

    /// <summary>An amount of zero.</summary>
    public static Amount Zero => default;

    /// <summary>The raw count of milli-units.</summary>
    public long Milli { get; }

    /// <summary>The number of whole units, rounded toward negative infinity (so -0.001 is -1).</summary>
    public long WholeUnits
    {
        get
        {
            long units = Milli / MilliPerUnit;
            return Milli % MilliPerUnit < 0 ? units - 1 : units;
        }
    }

    /// <summary>Creates an amount from whole units.</summary>
    /// <param name="units">The number of units.</param>
    /// <returns>The amount.</returns>
    /// <exception cref="OverflowException">The value does not fit once converted to milli-units.</exception>
    public static Amount FromUnits(long units) => new(checked(units * MilliPerUnit));

    /// <summary>Creates an amount from a raw count of milli-units.</summary>
    /// <param name="milli">The number of milli-units.</param>
    /// <returns>The amount.</returns>
    public static Amount FromMilli(long milli) => new(milli);

    /// <summary>
    /// Parses a decimal such as <c>12.5</c>, <c>-3.25</c> or <c>0.001</c>. The grammar is an optional
    /// <c>-</c>, one or more ASCII digits and, optionally, a <c>.</c> followed by one to three digits.
    /// The parse never depends on the current culture, and there is no exponent, thousands separator,
    /// <c>+</c> sign or whitespace.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The amount.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text does not match the grammar, including more than three decimals.</exception>
    /// <exception cref="OverflowException">The value does not fit in an <see cref="Amount"/>.</exception>
    public static Amount Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Validate the shape first so a malformed string is always a FormatException, never an overflow.
        ReadOnlySpan<char> span = text;
        bool negative = span.Length > 0 && span[0] == '-';
        ReadOnlySpan<char> rest = negative ? span[1..] : span;

        int point = rest.IndexOf('.');
        ReadOnlySpan<char> whole = point < 0 ? rest : rest[..point];
        ReadOnlySpan<char> fraction = point < 0 ? default : rest[(point + 1)..];

        if (whole.IsEmpty || !AllAsciiDigits(whole) || (point >= 0 && (fraction.IsEmpty || !AllAsciiDigits(fraction))))
        {
            throw new FormatException($"'{text}' is not an Amount: expected [-]digits[.digits].");
        }

        if (fraction.Length > MaxDecimals)
        {
            throw new FormatException($"'{text}' has more than {MaxDecimals} decimals; Amount holds milli-units.");
        }

        // Accumulate with the sign applied so long.MinValue milli parses without a spurious overflow.
        long milli = 0;
        foreach (char c in whole)
        {
            milli = Accumulate(milli, c, negative);
        }

        foreach (char c in fraction)
        {
            milli = Accumulate(milli, c, negative);
        }

        for (int missing = fraction.Length; missing < MaxDecimals; missing++)
        {
            milli = checked(milli * 10);
        }

        return new Amount(milli);
    }

    /// <summary>Adds two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The sum does not fit.</exception>
    public static Amount operator +(Amount left, Amount right) => new(checked(left.Milli + right.Milli));

    /// <summary>Subtracts one amount from another.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The difference does not fit.</exception>
    public static Amount operator -(Amount left, Amount right) => new(checked(left.Milli - right.Milli));

    /// <summary>Negates an amount.</summary>
    /// <param name="value">The amount to negate.</param>
    /// <returns>The negated amount.</returns>
    /// <exception cref="OverflowException">The value is the smallest representable amount.</exception>
    public static Amount operator -(Amount value) => new(checked(-value.Milli));

    /// <summary>Tests whether one amount is smaller than another.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is smaller.</returns>
    public static bool operator <(Amount left, Amount right) => left.Milli < right.Milli;

    /// <summary>Tests whether one amount is larger than another.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is larger.</returns>
    public static bool operator >(Amount left, Amount right) => left.Milli > right.Milli;

    /// <summary>Tests whether one amount is smaller than or equal to another.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is not larger.</returns>
    public static bool operator <=(Amount left, Amount right) => left.Milli <= right.Milli;

    /// <summary>Tests whether one amount is larger than or equal to another.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is not smaller.</returns>
    public static bool operator >=(Amount left, Amount right) => left.Milli >= right.Milli;

    /// <summary>Returns the smaller of two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The smaller amount.</returns>
    public static Amount Min(Amount left, Amount right) => left.Milli <= right.Milli ? left : right;

    /// <summary>Returns the larger of two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The larger amount.</returns>
    public static Amount Max(Amount left, Amount right) => left.Milli >= right.Milli ? left : right;

    /// <summary>Multiplies this amount by a ratio, rounding toward negative infinity.</summary>
    /// <param name="ratio">The multiplier.</param>
    /// <returns>The scaled amount.</returns>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public Amount MultiplyBy(Ratio ratio) => MulDiv(ratio.PerMille, 1000);

    /// <summary>
    /// Computes <c>this * numerator / denominator</c> with the product held in 128 bits, so only a result
    /// that does not fit in an <see cref="Amount"/> overflows. The quotient rounds toward negative infinity.
    /// </summary>
    /// <param name="numerator">The factor to multiply by.</param>
    /// <param name="denominator">The divisor, which must not be zero.</param>
    /// <returns>The scaled amount.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="denominator"/> is zero.</exception>
    /// <exception cref="OverflowException">The result does not fit.</exception>
    public Amount MulDiv(long numerator, long denominator)
    {
        Int128 product = (Int128)Milli * numerator;
        (Int128 quotient, Int128 remainder) = Int128.DivRem(product, denominator);

        // Division truncates toward zero; step down when the exact result is negative and inexact.
        if (remainder != 0 && (product < 0) != (denominator < 0))
        {
            quotient -= 1;
        }

        return new Amount(checked((long)quotient));
    }

    /// <inheritdoc />
    public int CompareTo(Amount other) => Milli.CompareTo(other.Milli);

    /// <summary>
    /// Formats the amount in plain decimal with trailing zeros removed, for example <c>12.5</c>, <c>-3.25</c>,
    /// <c>0.001</c> or <c>12</c>. The output never depends on the current culture and parses back to the same amount.
    /// </summary>
    /// <returns>The decimal text.</returns>
    public override string ToString()
    {
        // Longest output: sign, 16 whole digits, point, 3 decimals.
        Span<char> buffer = stackalloc char[24];
        int position = buffer.Length;

        ulong magnitude = Milli < 0 ? unchecked(0UL - (ulong)Milli) : (ulong)Milli;
        ulong whole = magnitude / MilliPerUnit;
        int fraction = (int)(magnitude % MilliPerUnit);

        if (fraction != 0)
        {
            int digits = MaxDecimals;
            while (fraction % 10 == 0)
            {
                fraction /= 10;
                digits--;
            }

            for (int i = 0; i < digits; i++)
            {
                buffer[--position] = (char)('0' + (fraction % 10));
                fraction /= 10;
            }

            buffer[--position] = '.';
        }

        do
        {
            buffer[--position] = (char)('0' + (int)(whole % 10));
            whole /= 10;
        }
        while (whole != 0);

        if (Milli < 0)
        {
            buffer[--position] = '-';
        }

        return new string(buffer[position..]);
    }

    private static bool AllAsciiDigits(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static long Accumulate(long value, char digit, bool negative)
    {
        int d = digit - '0';
        return negative ? checked((value * 10) - d) : checked((value * 10) + d);
    }
}
