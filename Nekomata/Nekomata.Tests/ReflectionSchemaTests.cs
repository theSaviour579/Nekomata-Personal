using System.Text.Json;
using Nekomata.AI.Schemas;
using Xunit;

namespace Nekomata.Tests;

public sealed class ReflectionSchemaTests
{
    private sealed class Inner { public string Text { get; set; } = ""; public int? Count { get; set; } }
    private sealed class Outer
    {
        public string Summary { get; set; } = "";
        public bool NeedsReply { get; set; }
        public decimal Cost { get; set; }
        public List<Inner> Items { get; set; } = [];
        public DayOfWeek Day { get; set; }
    }

    [Fact]
    public void Builds_a_strict_schema_from_a_result_class()
    {
        var (name, json) = ReflectionSchema.For(typeof(Outer));
        Assert.Equal("outer", name);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(5, root.GetProperty("required").GetArrayLength());
        var props = root.GetProperty("properties");
        Assert.Equal("boolean", props.GetProperty("needsReply").GetProperty("type").GetString());
        Assert.Equal("number", props.GetProperty("cost").GetProperty("type").GetString());
        Assert.Equal("string", props.GetProperty("day").GetProperty("type").GetString());
        var item = props.GetProperty("items").GetProperty("items");
        Assert.Equal("object", item.GetProperty("type").GetString());
        Assert.Equal("null", item.GetProperty("properties").GetProperty("count").GetProperty("type")[1].GetString());
    }
}
