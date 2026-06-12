using System.Text.Json;
using Aep.Server.Configuration;
using Aep.Server.Http;
using Aep.Server.OpenApi;
using Aep.Storage.Abstractions.Model;

namespace Aep.Server.Tests;

/// <summary>
/// Covers AEP-203 field behaviors (#02): immutable, input_only, output_only — parsed from
/// YAML, enforced by the validator, stripped from responses, and surfaced in the OpenAPI spec.
/// </summary>
public sealed class FieldBehaviorTests
{
    private const string Yaml = """
        name: "test.example.com"
        resources:
          widget:
            singular: "widget"
            plural: "widgets"
            schema:
              type: object
              required: ["name"]
              properties:
                name:    { type: string }
                serial:  { type: string, immutable: true }
                secret:  { type: string, input_only: true }
                state:   { type: string, enum: ["ON", "OFF"] }
                token:   { type: string, read_only: true }
                count32: { type: integer, format: int32 }
                count64: { type: integer, format: int64 }
                tags:    { type: array, items: { type: string } }
                levels:  { type: array, items: { type: string, enum: ["LOW", "HIGH"] } }
        """;

    private static ResourceDefinition Widget()
    {
        var registry = new ResourceRegistry(ServiceDefinitionLoader.Parse(Yaml));
        return registry.Get("widget");
    }

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Loader_parses_field_behaviors()
    {
        var props = Widget().Schema.Properties;
        Assert.True(props["serial"].Immutable);
        Assert.True(props["secret"].InputOnly);
        Assert.True(props["token"].ReadOnly);
        Assert.Equal(new[] { "ON", "OFF" }, props["state"].Enum);
    }

    [Fact]
    public void Patch_rejects_immutable_field()
    {
        var ex = Assert.Throws<ResourceValidationException>(() =>
            SchemaValidator.ValidateForPatch(Widget(), Body("""{"serial":"new"}""")));
        Assert.Contains("immutable", ex.Message);
    }

    [Fact]
    public void Patch_rejects_clearing_immutable_field()
    {
        Assert.Throws<ResourceValidationException>(() =>
            SchemaValidator.ValidateForPatch(Widget(), Body("""{"serial":null}""")));
    }

    [Fact]
    public void Create_allows_immutable_field()
    {
        var fields = SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","serial":"abc"}"""));
        Assert.True(fields.ContainsKey("serial"));
    }

    [Fact]
    public void Patch_allows_other_fields_when_immutable_absent()
    {
        var fields = SchemaValidator.ValidateForPatch(Widget(), Body("""{"name":"renamed"}"""));
        Assert.Equal("renamed", ((JsonElement)fields["name"]!).GetString());
    }

    [Fact]
    public void Int32_rejects_out_of_range_value()
    {
        var ex = Assert.Throws<ResourceValidationException>(() =>
            SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","count32":3000000000}""")));
        Assert.Contains("int32", ex.Message);
    }

    [Fact]
    public void Int64_accepts_value_beyond_int32()
    {
        var fields = SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","count64":3000000000}"""));
        Assert.True(fields.ContainsKey("count64"));
    }

    [Fact]
    public void Array_items_are_type_checked()
    {
        var ex = Assert.Throws<ResourceValidationException>(() =>
            SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","tags":["ok",42]}""")));
        Assert.Contains("tags[1]", ex.Message);
    }

    [Fact]
    public void Array_item_enums_are_enforced()
    {
        Assert.Throws<ResourceValidationException>(() =>
            SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","levels":["LOW","MID"]}""")));
        var ok = SchemaValidator.ValidateForWrite(Widget(), Body("""{"name":"w","levels":["LOW","HIGH"]}"""));
        Assert.True(ok.ContainsKey("levels"));
    }

    [Fact]
    public void Input_only_field_is_stripped_from_responses()
    {
        var stored = new StoredResource
        {
            Id = "w1",
            Path = "widgets/w1",
            CreateTime = "2026-01-01T00:00:00Z",
            UpdateTime = "2026-01-01T00:00:00Z",
            Fields = new() { ["name"] = "w", ["secret"] = "hunter2" },
        };

        var body = ResourceResponse.ToBody(stored, Widget());

        Assert.Equal("w", body["name"]);
        Assert.False(body.ContainsKey("secret")); // input_only: accepted on write, never returned
    }

    [Fact]
    public void OpenApi_surfaces_field_behaviors()
    {
        var registry = new ResourceRegistry(ServiceDefinitionLoader.Parse(Yaml));
        var doc = new OpenApiGenerator(registry).Generate();
        var props = doc["components"]!["schemas"]!["widget"]!["properties"]!;

        Assert.Equal("IMMUTABLE", props["serial"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
        Assert.Equal("INPUT_ONLY", props["secret"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
        Assert.True(props["secret"]!["writeOnly"]!.GetValue<bool>());
        Assert.Equal("OUTPUT_ONLY", props["token"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
        Assert.True(props["token"]!["readOnly"]!.GetValue<bool>());

        // Standard server-managed fields are marked output-only too.
        Assert.Equal("OUTPUT_ONLY", props["create_time"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
        Assert.Equal("OUTPUT_ONLY", props["id"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
        Assert.Equal("OUTPUT_ONLY", props["uid"]!["x-aep-field-behavior"]![0]!.GetValue<string>());
    }
}
