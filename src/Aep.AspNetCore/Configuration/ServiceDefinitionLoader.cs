using Aep.Storage.Abstractions.Model;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aep.Server.Configuration;

/// <summary>
/// Loads an aepc-format <c>resources.yaml</c> into a <see cref="ServiceDefinition"/>.
/// Unknown keys (e.g. aepc's <c>x-aep-field</c>) are ignored. URL patterns are not
/// computed here — that is the registry's job once all resources are known.
/// </summary>
public static class ServiceDefinitionLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ServiceDefinition LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"resources file not found: {path}", path);
        return Parse(File.ReadAllText(path));
    }

    public static ServiceDefinition Parse(string yaml)
    {
        var dto = Deserializer.Deserialize<ServiceYaml>(yaml)
            ?? throw new InvalidOperationException("resources.yaml is empty");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("resources.yaml: 'name' is required");
        if (dto.Resources is null || dto.Resources.Count == 0)
            throw new InvalidOperationException("resources.yaml: at least one resource is required");

        var resources = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        foreach (var (key, r) in dto.Resources)
        {
            var singular = r.Singular ?? key;
            var plural = r.Plural ?? singular + "s";
            resources[singular] = new ResourceDefinition
            {
                Singular = singular,
                Plural = plural,
                Parents = r.Parents ?? [],
                Description = r.Description,
                Schema = MapSchema(r.Schema, singular),
                Methods = MapMethods(r.Methods),
                NotImplemented = r.NotImplemented ?? false,
            };
        }

        return new ServiceDefinition
        {
            Name = dto.Name,
            ServerUrl = dto.ServerUrl,
            Contact = dto.Contact is null ? null : new Contact
            {
                Name = dto.Contact.Name,
                Email = dto.Contact.Email,
                Url = dto.Contact.Url,
            },
            Resources = resources,
        };
    }

    private static ResourceSchema MapSchema(SchemaYaml? schema, string singular)
    {
        if (schema?.Properties is null)
            return new ResourceSchema { Required = schema?.Required ?? [] };

        var props = new Dictionary<string, SchemaProperty>(StringComparer.Ordinal);
        foreach (var (name, p) in schema.Properties)
            props[name] = MapProperty(p);

        return new ResourceSchema
        {
            Type = schema.Type ?? "object",
            Properties = props,
            Required = schema.Required ?? [],
        };
    }

    private static SchemaProperty MapProperty(PropertyYaml p) => new()
    {
        Type = p.Type ?? "string",
        Format = p.Format,
        ReadOnly = p.ReadOnly ?? false,
        Immutable = p.Immutable ?? false,
        InputOnly = p.InputOnly ?? false,
        Description = p.Description,
        Enum = p.Enum,
        Items = p.Items is null ? null : MapProperty(p.Items),
    };

    private static ResourceMethods MapMethods(MethodsYaml? m)
    {
        // No methods block => full CRUDL+Apply (friendly default for a static server).
        if (m is null)
            return new ResourceMethods();

        return new ResourceMethods
        {
            Get = m.Get is not null,
            List = m.List is not null,
            Create = m.Create is not null,
            Update = m.Update is not null,
            Delete = m.Delete is not null,
            Apply = m.Apply is not null,
            // Flags default to true when their method is present but the flag is omitted.
            SupportsUserSettableCreate = m.Create?.SupportsUserSettableCreate ?? true,
            SupportsFilter = m.List?.SupportsFilter ?? true,
            SupportsSkip = m.List?.SupportsSkip ?? true,
        };
    }

    // ---- YAML DTOs (snake_case via UnderscoredNamingConvention) ----

    private sealed class ServiceYaml
    {
        public string? Name { get; set; }
        public string? ServerUrl { get; set; }
        public ContactYaml? Contact { get; set; }
        public Dictionary<string, ResourceYaml>? Resources { get; set; }
    }

    private sealed class ContactYaml
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Url { get; set; }
    }

    private sealed class ResourceYaml
    {
        public string? Singular { get; set; }
        public string? Plural { get; set; }
        public List<string>? Parents { get; set; }
        public string? Description { get; set; }
        public SchemaYaml? Schema { get; set; }
        public MethodsYaml? Methods { get; set; }
        public bool? NotImplemented { get; set; }
    }

    private sealed class SchemaYaml
    {
        public string? Type { get; set; }
        public List<string>? Required { get; set; }
        public Dictionary<string, PropertyYaml>? Properties { get; set; }
    }

    private sealed class PropertyYaml
    {
        public string? Type { get; set; }
        public string? Format { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? Immutable { get; set; }
        public bool? InputOnly { get; set; }
        public string? Description { get; set; }
        public List<string>? Enum { get; set; }
        public PropertyYaml? Items { get; set; }
    }

    private sealed class MethodsYaml
    {
        public CreateYaml? Create { get; set; }
        public ListYaml? List { get; set; }
        public object? Get { get; set; }
        public object? Update { get; set; }
        public object? Delete { get; set; }
        public object? Apply { get; set; }
    }

    private sealed class CreateYaml
    {
        public bool? SupportsUserSettableCreate { get; set; }
    }

    private sealed class ListYaml
    {
        public bool? SupportsFilter { get; set; }
        public bool? SupportsSkip { get; set; }
    }
}
