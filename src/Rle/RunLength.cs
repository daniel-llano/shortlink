using System.Text;

namespace Rle;

/// <summary>
/// Run-length encoding: "aaabcc" becomes "3a1b2c". Counts are written in decimal, so the input can't contain digits.
/// </summary>
public static class RunLength
{
    public static string Encode(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Any(char.IsDigit))
            throw new ArgumentException("Input can't contain digits, because counts are written as digits.", nameof(input));
        if (input.Length == 0) return "";

        var result = new StringBuilder(input.Length);
        var count = 1;
        for (var i = 1; i <= input.Length; i++)
        {
            if (i < input.Length && input[i] == input[i - 1])
            {
                count++;
                continue;
            }
            result.Append(count).Append(input[i - 1]);
            count = 1;
        }
        return result.ToString();
    }

    public static string Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var result = new StringBuilder();
        var i = 0;
        while (i < encoded.Length)
        {
            var start = i;
            while (i < encoded.Length && char.IsDigit(encoded[i])) i++;
            if (i == start || i == encoded.Length)
                throw new FormatException($"Expected a count followed by a character at position {start}.");

            var count = int.Parse(encoded.AsSpan(start, i - start));
            if (count == 0)
                throw new FormatException($"Count at position {start} must be at least 1.");
            result.Append(encoded[i], count);
            i++;
        }
        return result.ToString();
    }
}
