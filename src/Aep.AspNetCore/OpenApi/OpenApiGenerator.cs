using System.Text.Json.Nodes;
using Aep.Server.Configuration;
using Aep.Storage.Abstractions.Model;

namespace Aep.Server.OpenApi;

/// <summary>
/// Generates an AEP-flavored OpenAPI 3.1 document from the resource registry.
/// Each resource gets a component schema annotated with <c>x-aep-resource</c>
/// (singular/plural/patterns/parents/type, matching aep-lib-go) plus collection
/// and item paths for its enabled standard methods. Compatible with aepcli and
/// ui.aep.dev.
/// </summary>
public sealed class OpenApiGenerator(IResourceRegistry registry)
{
    private const string ErrorSchema = "#/components/schemas/Error";

    public JsonObject Generate()
    {
        var service = registry.Service;
        var schemas = new JsonObject { ["Error"] = BuildErrorSchema() };
        var paths = new JsonObject();
        var tags = new JsonArray();

        foreach (var r in registry.All)
        {
            // A not-implemented resource is a routing parent only — no operations to advertise.
            // Children still reference it by name (their paths carry its plural segment).
            if (r.NotImplemented)
                continue;

            schemas[r.Singular] = BuildResourceSchema(r, service.Name);
            AddPaths(paths, r);
            tags.Add(new JsonObject
            {
                ["name"] = r.Singular,
                ["description"] = r.Description ?? $"Operations on {r.Plural}.",
            });
        }

        var info = new JsonObject
        {
            ["title"] = service.Name,
            ["version"] = "0.1.0",
            ["description"] = "An AEP-compliant API generated from resources.yaml.",
        };
        if (service.Contact is { } c)
        {
            var contact = new JsonObject();
            if (c.Name is not null) contact["name"] = c.Name;
            if (c.Email is not null) contact["email"] = c.Email;
            if (c.Url is not null) contact["url"] = c.Url;
            info["contact"] = contact;
        }

        var doc = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = info,
            ["tags"] = tags,
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas },
        };
        if (!string.IsNullOrEmpty(service.ServerUrl))
            doc["servers"] = new JsonArray { new JsonObject { ["url"] = service.ServerUrl } };

        return doc;
    }

    private static JsonObject BuildResourceSchema(ResourceDefinition r, string apiName)
    {
        var properties = new JsonObject
        {
            ["id"] = OutputOnlyString("The resource id."),
            ["uid"] = OutputOnlyString("A system-assigned unique identifier, stable for the resource's lifetime."),
            ["path"] = OutputOnlyString("The full resource path."),
            ["create_time"] = TimestampSchema("The time the resource was created."),
            ["update_time"] = TimestampSchema("The time the resource was last updated."),
        };
        foreach (var (name, prop) in r.Schema.Properties)
            properties[name] = PropertySchema(prop);

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["x-aep-resource"] = new JsonObject
            {
                ["singular"] = r.Singular,
                ["plural"] = r.Plural,
                ["patterns"] = new JsonArray { r.ItemPattern },
                ["parents"] = new JsonArray(r.Parents.Select(p => (JsonNode)p!).ToArray()),
                ["type"] = $"{apiName}/{r.Singular}",
            },
            ["properties"] = properties,
        };
        if (r.Description is not null)
            schema["description"] = r.Description;
        if (r.Schema.Required.Count > 0)
            schema["required"] = new JsonArray(r.Schema.Required.Select(s => (JsonNode)s!).ToArray());
        return schema;
    }

    private static JsonObject PropertySchema(SchemaProperty prop)
    {
        var o = new JsonObject { ["type"] = prop.Type };
        if (prop.Format is not null) o["format"] = prop.Format;
        if (prop.Description is not null) o["description"] = prop.Description;
        if (prop.ReadOnly) o["readOnly"] = true;
        if (prop.InputOnly) o["writeOnly"] = true;
        var behaviors = FieldBehaviors(prop);
        if (behaviors is not null) o["x-aep-field-behavior"] = behaviors;
        if (prop.Enum is { Count: > 0 })
            o["enum"] = new JsonArray(prop.Enum.Select(e => (JsonNode)e!).ToArray());
        if (prop is { Type: "array", Items: not null })
            o["items"] = PropertySchema(prop.Items);
        return o;
    }

    /// <summary>AEP-203 field behaviors as the array aep-lib-go reads (<c>x-aep-field-behavior</c>).</summary>
    private static JsonArray? FieldBehaviors(SchemaProperty prop)
    {
        var behaviors = new JsonArray();
        if (prop.ReadOnly) behaviors.Add("OUTPUT_ONLY");
        if (prop.Immutable) behaviors.Add("IMMUTABLE");
        if (prop.InputOnly) behaviors.Add("INPUT_ONLY");
        return behaviors.Count > 0 ? behaviors : null;
    }

    private void AddPaths(JsonObject paths, ResourceDefinition r)
    {
        var refNode = new JsonObject { ["$ref"] = $"#/components/schemas/{r.Singular}" };

        var collection = new JsonObject();
        if (r.Methods.List) collection["get"] = ListOperation(r, refNode.DeepClone().AsObject());
        if (r.Methods.Create) collection["post"] = CreateOperation(r, refNode.DeepClone().AsObject());
        if (collection.Count > 0)
            paths["/" + r.CollectionPattern] = collection;

        var item = new JsonObject();
        if (r.Methods.Get) item["get"] = ItemOperation(r, "Get", $"Get a {r.Singular}.", refNode.DeepClone().AsObject(), notFound: true);
        // PATCH (JSON Merge Patch, AEP-134) references the resource schema, matching aep-lib-go.
        // The server still applies it partially; see docs/KNOWN_ISSUES.md on required-on-PATCH.
        if (r.Methods.Update) item["patch"] = WriteItemOperation(r, "Update", $"Update a {r.Singular}.",
            refNode.DeepClone().AsObject(), requestSchema: refNode.DeepClone().AsObject(), notFound: true,
            requestContentType: "application/merge-patch+json");
        // PUT replaces the whole resource, so it uses the full schema (required fields enforced).
        if (r.Methods.Apply) item["put"] = WriteItemOperation(r, "Apply", $"Create or replace a {r.Singular}.",
            refNode.DeepClone().AsObject(), requestSchema: refNode.DeepClone().AsObject(), notFound: false);
        if (r.Methods.Delete) item["delete"] = DeleteOperation(r);
        if (item.Count > 0)
            paths["/" + r.ItemPattern] = item;
    }

    private static JsonObject ListOperation(ResourceDefinition r, JsonObject itemRef)
    {
        var parameters = PathParameters(r, includeId: false);
        parameters.Add(QueryParam("max_page_size", "integer", "Maximum number of results per page."));
        parameters.Add(QueryParam("page_token", "string", "Continuation token from a previous response."));
        if (r.Methods.SupportsSkip) parameters.Add(QueryParam("skip", "integer", "Number of results to skip."));
        if (r.Methods.SupportsFilter) parameters.Add(QueryParam("filter", "string", "Filter expression (AEP-160)."));

        var listSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["results"] = new JsonObject { ["type"] = "array", ["items"] = itemRef },
                ["next_page_token"] = new JsonObject { ["type"] = "string", ["description"] = "Token for the next page; absent when there are no more results." },
            },
        };

        return new JsonObject
        {
            ["operationId"] = $"List{Pascal(r.Singular)}",
            ["tags"] = new JsonArray { r.Singular },
            ["description"] = $"List {r.Plural}.",
            ["parameters"] = parameters,
            ["responses"] = new JsonObject { ["200"] = JsonResponse($"A page of {r.Plural}.", listSchema) },
        };
    }

    private static JsonObject CreateOperation(ResourceDefinition r, JsonObject itemRef)
    {
        var parameters = PathParameters(r, includeId: false);
        if (r.Methods.SupportsUserSettableCreate)
            parameters.Add(QueryParam("id", "string", "Client-supplied id for the new resource."));

        return new JsonObject
        {
            ["operationId"] = $"Create{Pascal(r.Singular)}",
            ["tags"] = new JsonArray { r.Singular },
            ["description"] = $"Create a {r.Singular}.",
            ["parameters"] = parameters,
            ["requestBody"] = JsonRequestBody(itemRef.DeepClone().AsObject()),
            ["responses"] = new JsonObject
            {
                ["200"] = WithETagHeader(JsonResponse($"The created {r.Singular}.", itemRef)),
                ["400"] = ErrorResponse("The request body is invalid."),
                ["409"] = ErrorResponse($"A {r.Singular} with the given id already exists."),
            },
        };
    }

    private static JsonObject ItemOperation(ResourceDefinition r, string action, string description, JsonObject itemRef, bool notFound)
    {
        var responses = new JsonObject { ["200"] = WithETagHeader(JsonResponse($"The {r.Singular}.", itemRef)) };
        if (notFound) responses["404"] = ErrorResponse($"The {r.Singular} was not found.");
        responses["412"] = ErrorResponse("The If-Match precondition failed (AEP-154).");

        var parameters = PathParameters(r, includeId: true);
        parameters.Add(IfMatchParam());
        return new JsonObject
        {
            ["operationId"] = $"{action}{Pascal(r.Singular)}",
            ["tags"] = new JsonArray { r.Singular },
            ["description"] = description,
            ["parameters"] = parameters,
            ["responses"] = responses,
        };
    }

    private static JsonObject WriteItemOperation(
        ResourceDefinition r, string action, string description,
        JsonObject responseRef, JsonObject requestSchema, bool notFound,
        string requestContentType = "application/json")
    {
        var op = ItemOperation(r, action, description, responseRef, notFound);
        op["requestBody"] = JsonRequestBody(requestSchema, requestContentType);
        op["responses"]!.AsObject()["400"] = ErrorResponse("The request body is invalid.");
        return op;
    }

    private static JsonObject DeleteOperation(ResourceDefinition r)
    {
        var parameters = PathParameters(r, includeId: true);
        parameters.Add(IfMatchParam());
        return new JsonObject
        {
            ["operationId"] = $"Delete{Pascal(r.Singular)}",
            ["tags"] = new JsonArray { r.Singular },
            ["description"] = $"Delete a {r.Singular}.",
            ["parameters"] = parameters,
            ["responses"] = new JsonObject
            {
                ["204"] = new JsonObject { ["description"] = $"The {r.Singular} was deleted." },
                ["404"] = ErrorResponse($"The {r.Singular} was not found."),
                ["412"] = ErrorResponse("The If-Match precondition failed (AEP-154)."),
            },
        };
    }

    private static JsonObject IfMatchParam() => new()
    {
        ["name"] = "If-Match",
        ["in"] = "header",
        ["required"] = false,
        ["schema"] = new JsonObject { ["type"] = "string" },
        ["description"] = "Optimistic-concurrency precondition (AEP-154): the expected ETag, or \"*\" for any current version.",
    };

    /// <summary>Adds the <c>ETag</c> response header to a single-resource 200 response.</summary>
    private static JsonObject WithETagHeader(JsonObject response)
    {
        response["headers"] = new JsonObject
        {
            ["ETag"] = new JsonObject
            {
                ["description"] = "The entity tag of the returned resource (AEP-154).",
                ["schema"] = new JsonObject { ["type"] = "string" },
            },
        };
        return response;
    }

    private static JsonArray PathParameters(ResourceDefinition r, bool includeId)
    {
        var elems = r.PatternElems;
        var count = includeId ? elems.Count : elems.Count - 1; // drop the trailing id segment for collections
        var parameters = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var elem = elems[i];
            if (!elem.StartsWith('{')) continue;
            var name = elem.Trim('{', '}');
            parameters.Add(new JsonObject
            {
                ["name"] = name,
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JsonObject { ["type"] = "string" },
                ["description"] = $"The id of the {name[..^3]}.", // strip "_id"
            });
        }
        return parameters;
    }

    private static JsonObject QueryParam(string name, string type, string description) => new()
    {
        ["name"] = name,
        ["in"] = "query",
        ["required"] = false,
        ["schema"] = new JsonObject { ["type"] = type },
        ["description"] = description,
    };

    private static JsonObject JsonRequestBody(JsonObject schema, string contentType = "application/json") => new()
    {
        ["required"] = true,
        ["content"] = new JsonObject { [contentType] = new JsonObject { ["schema"] = schema } },
    };

    private static JsonObject JsonResponse(string description, JsonObject schema) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = schema } },
    };

    private static JsonObject ErrorResponse(string description) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject
        {
            ["application/problem+json"] = new JsonObject { ["schema"] = new JsonObject { ["$ref"] = ErrorSchema } },
        },
    };

    private static JsonObject BuildErrorSchema() => new()
    {
        ["type"] = "object",
        ["description"] = "An AEP-193 error response, following RFC 9457 Problem Details.",
        ["required"] = new JsonArray { "type" },
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference", ["description"] = "A URI reference identifying the problem type." },
            ["status"] = new JsonObject { ["type"] = "integer", ["description"] = "The HTTP status code." },
            ["title"] = new JsonObject { ["type"] = "string", ["description"] = "A human-readable summary of the problem type." },
            ["detail"] = new JsonObject { ["type"] = "string", ["description"] = "A human-readable explanation specific to this occurrence." },
            ["instance"] = new JsonObject { ["type"] = "string", ["format"] = "uri-reference", ["description"] = "A URI reference identifying this specific occurrence." },
        },
    };

    private static JsonObject TimestampSchema(string description) => new()
    {
        ["type"] = "string",
        ["format"] = "date-time",
        ["readOnly"] = true,
        ["x-aep-field-behavior"] = new JsonArray { "OUTPUT_ONLY" },
        ["description"] = description,
    };

    private static JsonObject OutputOnlyString(string description) => new()
    {
        ["type"] = "string",
        ["readOnly"] = true,
        ["x-aep-field-behavior"] = new JsonArray { "OUTPUT_ONLY" },
        ["description"] = description,
    };

    private static string Pascal(string singular) =>
        string.Concat(singular.Split('-', '_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
}
