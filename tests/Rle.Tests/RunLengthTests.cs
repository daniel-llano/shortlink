using FluentAssertions;

namespace Rle.Tests;

public class RunLengthTests
{
    [Theory]
    [InlineData("aaabbbccc", "3a3b3c")]
    [InlineData("abc", "1a1b1c")]
    [InlineData("aaaa", "4a")]
    [InlineData("", "")]
    [InlineData("a", "1a")]
    [InlineData("abbbbbbbbbbbc", "1a11b1c")]
    [InlineData("  --", "2 2-")]
    public void Encode_And_Decode_RoundTrip(string plain, string encoded)
    {
        RunLength.Encode(plain).Should().Be(encoded);
        RunLength.Decode(encoded).Should().Be(plain);
    }

    [Fact]
    public void Encode_RejectsDigits()
    {
        var act = () => RunLength.Encode("a1");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("3")]
    [InlineData("0a")]
    public void Decode_RejectsMalformedInput(string encoded)
    {
        var act = () => RunLength.Decode(encoded);
        act.Should().Throw<FormatException>();
    }
}
