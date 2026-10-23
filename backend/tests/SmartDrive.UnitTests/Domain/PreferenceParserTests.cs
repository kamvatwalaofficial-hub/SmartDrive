using SmartDrive.Domain.Entities;
using SmartDrive.Domain.Recommendations;

namespace SmartDrive.UnitTests.Domain;

public sealed class PreferenceParserTests
{
    [Fact]
    public void Understands_the_example_from_the_brief()
    {
        var p = PreferenceParser.Parse(
            "I need a comfortable car for four adults and luggage for a five-day trip, and I want to spend no more than R600 per day.");

        Assert.Equal(4, p.Passengers);
        Assert.Equal(5, p.DurationDays);
        Assert.Equal(600m, p.MaxDailyBudget);
        Assert.Null(p.MaxTotalBudget);
        Assert.True(p.PrioritiseComfort);
        Assert.NotNull(p.LuggageBags);
        Assert.NotEmpty(p.Understood);
    }

    [Theory]
    [InlineData("automatic SUV for a road trip", "suv", TransmissionType.Automatic)]
    [InlineData("a cheap manual hatchback for the city", "hatchback", TransmissionType.Manual)]
    public void Picks_out_category_and_transmission(string text, string category, TransmissionType transmission)
    {
        var p = PreferenceParser.Parse(text);
        Assert.Equal(category, p.CategorySlug);
        Assert.Equal(transmission, p.Transmission);
    }

    [Fact]
    public void Says_so_when_it_understands_nothing()
    {
        var p = PreferenceParser.Parse("hello there");
        Assert.Null(p.Passengers);
        Assert.Null(p.MaxDailyBudget);
        Assert.NotEmpty(p.Notes);
    }

    [Fact]
    public void Out_of_range_values_are_rejected_not_trusted()
    {
        var p = new ParsedPreferences { Passengers = 400, DurationDays = 900, MaxDailyBudget = 5m }.Sanitised(out var rejected);
        Assert.Null(p.Passengers);
        Assert.Null(p.DurationDays);
        Assert.Null(p.MaxDailyBudget);
        Assert.Equal(3, rejected.Count);
    }
}
