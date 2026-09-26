using OtterLogic.StructuralForm;
using Xunit;

namespace OtterLogic.StructuralForm.Tests;

public class SpacingsTests
{
    [Fact]
    public void A_single_number_is_one_bay()
    {
        Assert.Equal(new[] { 6000.0 }, Spacings.Parse("6000"));
    }

    [Fact]
    public void A_count_an_x_and_a_spacing_repeats_the_bay()
    {
        Assert.Equal(new[] { 6000.0, 6000.0, 6000.0, 8000.0 }, Spacings.Parse("3x6000, 8000"));
    }

    [Theory]
    [InlineData("3X6000")]
    [InlineData("3*6000")]
    [InlineData("3x6000;")]
    [InlineData(" 6000 6000\t6000 ")]
    public void Capital_x_a_star_semicolons_and_whitespace_all_read_the_same(string text)
    {
        Assert.Equal(new[] { 6000.0, 6000.0, 6000.0 }, Spacings.Parse(text));
    }

    [Fact]
    public void The_decimal_mark_is_a_point_and_a_comma_is_always_a_separator()
    {
        Assert.Equal(new[] { 7.5, 7.5, 9.0 }, Spacings.Parse("2x7.5, 9"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-6000")]
    [InlineData("0x6000")]
    [InlineData("x6000")]
    [InlineData("2.5x6000")]
    [InlineData("6000, six")]
    [InlineData("NaN")]
    public void Anything_that_is_not_a_positive_bay_is_refused_with_a_reason(string text)
    {
        Assert.False(Spacings.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Throws<FormatException>(() => Spacings.Parse(text));
    }

    [Fact]
    public void Describe_collapses_runs_back_into_the_typed_form()
    {
        Assert.Equal("3x6000, 8000", Spacings.Describe(new[] { 6000.0, 6000.0, 6000.0, 8000.0 }));
        Assert.Equal("6000", Spacings.Describe(new[] { 6000.0 }));
        Assert.Equal("7.5, 2x9", Spacings.Describe(new[] { 7.5, 9.0, 9.0 }));
        Assert.Equal("2x6000,8000", Spacings.Describe(new[] { 6000.0, 6000.0, 8000.0 }, ","));
    }

    [Fact]
    public void Describe_and_parse_round_trip()
    {
        double[] bays = { 6.0, 6.0, 8.0, 8.0, 8.0, 6.0 };
        Assert.Equal(bays, Spacings.Parse(Spacings.Describe(bays)));
    }
}
