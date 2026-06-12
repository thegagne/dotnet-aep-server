using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Aep.Server.Tests;

public sealed class ResourceApiTests : IDisposable
{
    private readonly AepAppFactory _factory = new();
    private readonly HttpClient _client;

    public ResourceApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Full_crud_lifecycle()
    {
        // Create parent.
        var pub = await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme Press"}"""));
        Assert.Equal(HttpStatusCode.OK, pub.StatusCode);

        // Create nested book.
        var create = await _client.PostAsync("/publishers/acme/books?id=1984",
            Json("""{"title":"1984","author":"Orwell","price":1200,"published":true,"tags":["dystopia"]}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await BodyAsync(create);
        Assert.Equal("publishers/acme/books/1984", created.GetProperty("path").GetString());
        Assert.Equal("Orwell", created.GetProperty("author").GetString());
        Assert.Equal(1200, created.GetProperty("price").GetInt32());
        Assert.True(created.GetProperty("published").GetBoolean());
        Assert.Equal(JsonValueKind.Array, created.GetProperty("tags").ValueKind);

        // Get.
        var get = await _client.GetAsync("/publishers/acme/books/1984");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // Patch merges.
        var patch = await _client.PatchAsync("/publishers/acme/books/1984", Json("""{"author":"George Orwell"}"""));
        var patched = await BodyAsync(patch);
        Assert.Equal("George Orwell", patched.GetProperty("author").GetString());
        Assert.Equal("1984", patched.GetProperty("title").GetString()); // untouched

        // Delete.
        var del = await _client.DeleteAsync("/publishers/acme/books/1984");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var afterDelete = await _client.GetAsync("/publishers/acme/books/1984");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Uid_is_assigned_stable_and_not_client_settable()
    {
        // A client-supplied uid must be ignored; the server assigns its own.
        var create = await _client.PostAsync("/publishers?id=acme",
            Json("""{"display_name":"Acme","uid":"client-supplied"}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var uid = (await BodyAsync(create)).GetProperty("uid").GetString();
        Assert.False(string.IsNullOrEmpty(uid));
        Assert.NotEqual("client-supplied", uid);

        // Stable across a get and an update.
        var got = await BodyAsync(await _client.GetAsync("/publishers/acme"));
        Assert.Equal(uid, got.GetProperty("uid").GetString());
        var updated = await BodyAsync(await _client.PatchAsync("/publishers/acme", Json("""{"display_name":"Acme 2"}""")));
        Assert.Equal(uid, updated.GetProperty("uid").GetString());

        // Deleting and recreating the same id yields a different uid.
        await _client.DeleteAsync("/publishers/acme");
        var recreated = await BodyAsync(await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}""")));
        Assert.NotEqual(uid, recreated.GetProperty("uid").GetString());
    }

    [Fact]
    public async Task Nested_grandchild_resource_full_lifecycle()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        await _client.PostAsync("/publishers/acme/books?id=b1", Json("""{"title":"Book One"}"""));

        // Create a chapter (publisher -> book -> chapter, three levels deep).
        var create = await _client.PostAsync("/publishers/acme/books/b1/chapters?id=c1",
            Json("""{"title":"Intro","number":1}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await BodyAsync(create);
        Assert.Equal("publishers/acme/books/b1/chapters/c1", created.GetProperty("path").GetString());
        Assert.Equal(1, created.GetProperty("number").GetInt32());

        // Get via the full nested path.
        var get = await _client.GetAsync("/publishers/acme/books/b1/chapters/c1");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // List is scoped to the parent book.
        await _client.PostAsync("/publishers/acme/books/b1/chapters?id=c2", Json("""{"title":"Two","number":2}"""));
        var list = await BodyAsync(await _client.GetAsync("/publishers/acme/books/b1/chapters"));
        Assert.Equal(2, list.GetProperty("results").GetArrayLength());

        // A chapter under a different book is not listed here.
        await _client.PostAsync("/publishers/acme/books?id=b2", Json("""{"title":"Book Two"}"""));
        await _client.PostAsync("/publishers/acme/books/b2/chapters?id=cx", Json("""{"title":"Other"}"""));
        var stillTwo = await BodyAsync(await _client.GetAsync("/publishers/acme/books/b1/chapters"));
        Assert.Equal(2, stillTwo.GetProperty("results").GetArrayLength());

        // Delete and confirm.
        Assert.Equal(HttpStatusCode.NoContent,
            (await _client.DeleteAsync("/publishers/acme/books/b1/chapters/c1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/publishers/acme/books/b1/chapters/c1")).StatusCode);
    }

    [Fact]
    public async Task Nested_grandchild_in_openapi_spec()
    {
        var doc = await BodyAsync(await _client.GetAsync("/openapi.json"));
        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/publishers/{publisher_id}/books/{book_id}/chapters", out _));
        Assert.True(paths.TryGetProperty("/publishers/{publisher_id}/books/{book_id}/chapters/{chapter_id}", out _));

        var chapter = doc.GetProperty("components").GetProperty("schemas").GetProperty("chapter");
        var xaep = chapter.GetProperty("x-aep-resource");
        Assert.Equal("publishers/{publisher_id}/books/{book_id}/chapters/{chapter_id}",
            xaep.GetProperty("patterns")[0].GetString());
        Assert.Equal("book", xaep.GetProperty("parents")[0].GetString());
    }

    [Fact]
    public async Task Apply_creates_then_replaces()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));

        // PUT on a non-existent resource creates it.
        var create = await _client.PutAsync("/publishers/acme/books/b1",
            Json("""{"title":"First","author":"A"}"""));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal("A", (await BodyAsync(create)).GetProperty("author").GetString());

        // PUT again fully replaces — omitted fields are cleared.
        var replace = await _client.PutAsync("/publishers/acme/books/b1", Json("""{"title":"Second"}"""));
        var replaced = await BodyAsync(replace);
        Assert.Equal("Second", replaced.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("author").ValueKind);
    }

    [Fact]
    public async Task List_filters_and_paginates()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        for (var i = 0; i < 5; i++)
            await _client.PostAsync($"/publishers/acme/books?id=b{i}",
                Json($$"""{"title":"T{{i}}","author":"{{(i % 2 == 0 ? "Orwell" : "Huxley")}}","price":{{i * 10}}}"""));

        // Filter (CEL).
        var filtered = await BodyAsync(await _client.GetAsync(
            "/publishers/acme/books?filter=" + Uri.EscapeDataString("author == \"Orwell\" && price > 5")));
        var results = filtered.GetProperty("results");
        Assert.Equal(2, results.GetArrayLength()); // b2 (20), b4 (40)

        // Paginate.
        var page1 = await BodyAsync(await _client.GetAsync("/publishers/acme/books?max_page_size=2"));
        Assert.Equal(2, page1.GetProperty("results").GetArrayLength());
        var token = page1.GetProperty("next_page_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var page2 = await BodyAsync(await _client.GetAsync(
            $"/publishers/acme/books?max_page_size=2&page_token={Uri.EscapeDataString(token!)}"));
        Assert.Equal(2, page2.GetProperty("results").GetArrayLength());
    }

    [Theory]
    [InlineData("author == \"Orwell\"", 2)]
    [InlineData("price >= 1500", 2)]
    [InlineData("published == true", 2)]
    [InlineData("author != \"Orwell\"", 2)]
    [InlineData("author == \"Orwell\" && price > 1000", 1)]
    [InlineData("price < 1000 || price > 2000", 2)]
    [InlineData("(author == \"Orwell\" || author == \"Huxley\") && published == true", 2)]
    public async Task List_filter_operators(string filter, int expectedCount)
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        // Orwell/1200/pub, Orwell/900, Huxley/1500/pub, Huxley/3200
        await _client.PostAsync("/publishers/acme/books?id=b1", Json("""{"title":"a","author":"Orwell","price":1200,"published":true}"""));
        await _client.PostAsync("/publishers/acme/books?id=b2", Json("""{"title":"b","author":"Orwell","price":900}"""));
        await _client.PostAsync("/publishers/acme/books?id=b3", Json("""{"title":"c","author":"Huxley","price":1500,"published":true}"""));
        await _client.PostAsync("/publishers/acme/books?id=b4", Json("""{"title":"d","author":"Huxley","price":3200}"""));

        var body = await BodyAsync(await _client.GetAsync(
            "/publishers/acme/books?filter=" + Uri.EscapeDataString(filter)));
        Assert.Equal(expectedCount, body.GetProperty("results").GetArrayLength());
    }

    [Theory]
    [InlineData("price >")]            // malformed CEL
    [InlineData("price > 1 AND x")]    // AND keyword is not CEL
    [InlineData("bogus == 1")]         // valid CEL, unknown field
    public async Task List_invalid_filter_returns_400(string filter)
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));
        var response = await _client.GetAsync("/publishers/acme/books?filter=" + Uri.EscapeDataString(filter));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var err = await BodyAsync(response);
        Assert.Equal(400, err.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Validation_and_conflict_errors()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));

        // Missing required "title" -> 400 with AEP error body.
        var bad = await _client.PostAsync("/publishers/acme/books", Json("""{"author":"x"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var err = await BodyAsync(bad);
        Assert.Equal(400, err.GetProperty("status").GetInt32());

        // Wrong type -> 400.
        var wrongType = await _client.PostAsync("/publishers/acme/books?id=x",
            Json("""{"title":"t","price":"not-a-number"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);

        // Duplicate id -> 409.
        await _client.PostAsync("/publishers/acme/books?id=dup", Json("""{"title":"t"}"""));
        var dup = await _client.PostAsync("/publishers/acme/books?id=dup", Json("""{"title":"t2"}"""));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Create_reads_json_body_even_with_form_content_type()
    {
        await _client.PostAsync("/publishers?id=acme", Json("""{"display_name":"Acme"}"""));

        // A JSON payload sent with a form content type must still be parsed
        // (request buffering lets the controller re-read the raw body).
        var content = new StringContent("""{"title":"T","price":5}""", Encoding.UTF8,
            "application/x-www-form-urlencoded");
        var response = await _client.PostAsync("/publishers/acme/books?id=formy", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("T", (await BodyAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_unknown_returns_404_problem_details()
    {
        var response = await _client.GetAsync("/publishers/none/books/none");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // RFC 9457 Problem Details: application/problem+json with type/status/title/detail/instance.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var err = await BodyAsync(response);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", err.GetProperty("type").GetString());
        Assert.Equal(404, err.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", err.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(err.GetProperty("detail").GetString()));
        Assert.Equal("/publishers/none/books/none", err.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task OpenApi_spec_is_served_and_aep_annotated()
    {
        var response = await _client.GetAsync("/openapi.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await BodyAsync(response);

        Assert.Equal("3.1.0", doc.GetProperty("openapi").GetString());

        var paths = doc.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/publishers/{publisher_id}/books", out _));
        Assert.True(paths.TryGetProperty("/publishers/{publisher_id}/books/{book_id}", out _));

        var book = doc.GetProperty("components").GetProperty("schemas").GetProperty("book");
        var xaep = book.GetProperty("x-aep-resource");
        Assert.Equal("book", xaep.GetProperty("singular").GetString());
        Assert.Equal("books", xaep.GetProperty("plural").GetString());
        Assert.Equal("publishers/{publisher_id}/books/{book_id}",
            xaep.GetProperty("patterns")[0].GetString());
        Assert.Equal("publisher", xaep.GetProperty("parents")[0].GetString());

        // PATCH (JSON Merge Patch, AEP-134) references the resource schema under the merge-patch
        // media type, matching aep-lib-go; PUT (full replace) uses the same $ref under application/json.
        var item = paths.GetProperty("/publishers/{publisher_id}/books/{book_id}");
        var patchSchema = item.GetProperty("patch").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/merge-patch+json").GetProperty("schema");
        Assert.Equal("#/components/schemas/book", patchSchema.GetProperty("$ref").GetString());
        var putSchema = item.GetProperty("put").GetProperty("requestBody")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.Equal("#/components/schemas/book", putSchema.GetProperty("$ref").GetString());
    }
}
