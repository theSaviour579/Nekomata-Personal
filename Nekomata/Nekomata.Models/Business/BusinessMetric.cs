namespace Nekomata.Models.Business;

public class BusinessMetric
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Status { get; set; } = "Neutral";
    public string? Detail { get; set; }
}