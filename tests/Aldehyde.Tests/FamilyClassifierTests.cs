using Aldehyde.Data;

public class FamilyClassifierTests
{
    [Fact]
    public void SauvageIsGreen() =>
        Assert.Equal("green", FamilyClassifier.Classify(["fresh spicy", "amber", "citrus", "aromatic"]));

    [Fact]
    public void NoMappedAccordFallsBackToWoody() =>
        Assert.Equal("woody", FamilyClassifier.Classify(["fresh", "musky"]));

    [Fact]
    public void TieBreaksByHigherRankedAccord() =>
        Assert.Equal("citrus", FamilyClassifier.Classify(["citrus", "amber", "fresh", "fresh", "amber"]));
}
