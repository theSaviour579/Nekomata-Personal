namespace Nekomata.Services.KnowBe4;

public sealed class KnowBe4Options
{
    public string BaseUrl { get; set; } = "https://uk.api.knowbe4.com";
    public string ApiKey { get; set; } = string.Empty;
    public int LookbackHours { get; set; } = 24;
}