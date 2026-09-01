namespace AtspmDocsGenerator.Tests;

public sealed class DocumentationTextTests
{
    [Theory]
    [InlineData("plain", "`plain`")]
    [InlineData("value`with`ticks", "``value`with`ticks``")]
    [InlineData("`edge`", "`` `edge` ``")]
    public void CodeSpanUsesAFenceLongerThanItsContents(string value, string expected)
    {
        Assert.Equal(expected, DocumentationText.CodeSpan(value));
    }
}
