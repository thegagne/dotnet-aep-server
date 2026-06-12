// AepServer CLI — run an AEP API from a resources.yaml.
//
//   aep serve <resources.yaml> [--storage sqlite|inmemory] [--connection <cs>] [--urls <urls>]

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

if (args[0] != "serve")
{
    Console.Error.WriteLine($"unknown command '{args[0]}'.");
    PrintUsage();
    return 1;
}

if (args.Length < 2 || args[1].StartsWith('-'))
{
    Console.Error.WriteLine("error: 'serve' requires a path to a resources.yaml file.");
    PrintUsage();
    return 1;
}

var resourcesFile = args[1];
if (!File.Exists(resourcesFile))
{
    Console.Error.WriteLine($"error: resources file not found: {resourcesFile}");
    return 1;
}

var storage = "sqlite";
string? connection = null;
string? urls = null;
var overrides = new Dictionary<string, string?>();

for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--storage" or "-s": storage = Next(args, ref i); break;
        case "--connection" or "-c": connection = Next(args, ref i); break;
        case "--urls" or "-u": urls = Next(args, ref i); break;
        case "--set":
            var kv = Next(args, ref i);
            var eq = kv.IndexOf('=');
            if (eq <= 0)
            {
                Console.Error.WriteLine($"error: --set expects key=value, got '{kv}'.");
                return 1;
            }
            overrides[kv[..eq]] = kv[(eq + 1)..]; // e.g. Storage:Postgres:MaxPoolSize=20
            break;
        default:
            Console.Error.WriteLine($"error: unknown option '{args[i]}'.");
            PrintUsage();
            return 1;
    }
}

var settings = new Dictionary<string, string?>
{
    ["Resources:File"] = resourcesFile,
    ["Storage:Provider"] = storage,
};
if (connection is not null)
{
    // --connection is the SQL connection string, or the DynamoDB endpoint URL.
    settings[storage.ToLowerInvariant() switch
    {
        "postgres" => "Storage:Postgres:ConnectionString",
        "dynamodb" => "Storage:DynamoDb:ServiceUrl",
        _ => "Storage:Sqlite:ConnectionString",
    }] = connection;
}
if (urls is not null)
    settings["urls"] = urls; // honored by the host (same as ASPNETCORE_URLS)
foreach (var (key, value) in overrides)
    settings[key] = value; // --set wins over the convenience flags

var builder = WebApplication.CreateBuilder();
builder.Configuration.AddInMemoryCollection(settings);

builder.Services.AddAepServer(builder.Configuration);
switch (storage.ToLowerInvariant())
{
    case "inmemory":
        builder.Services.AddAepInMemoryStore();
        break;
    case "sqlite":
        builder.Services.AddAepSqliteStore(builder.Configuration);
        break;
    case "postgres":
        builder.Services.AddAepPostgresStore(builder.Configuration);
        break;
    case "dynamodb":
        builder.Services.AddAepDynamoDbStore(builder.Configuration);
        break;
    default:
        Console.Error.WriteLine($"error: unknown storage provider '{storage}' (expected: sqlite, inmemory, postgres, dynamodb).");
        return 1;
}

var app = builder.Build();
await app.MapAepServerAsync();
await app.RunAsync();
return 0;

static string Next(string[] args, ref int i)
{
    if (i + 1 >= args.Length)
        throw new ArgumentException($"option '{args[i]}' requires a value.");
    return args[++i];
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        AepServer — serve an AEP API from a resources.yaml.

        Usage:
          aep serve <resources.yaml> [options]

        Options:
          -s, --storage <name>        Backend: sqlite (default), inmemory, postgres, dynamodb
          -c, --connection <string>   SQL connection string, or the DynamoDB endpoint URL
          -u, --urls <urls>           Listen URLs (e.g. http://localhost:8080)
              --set <key=value>       Set any config key (repeatable), e.g. Storage:Postgres:MaxPoolSize=20

        Examples:
          aep serve ./resources.yaml
          aep serve ./resources.yaml --storage inmemory --urls http://localhost:8080
          aep serve ./resources.yaml --storage postgres --connection "Host=localhost;Database=aep;Username=postgres;Password=postgres"
          aep serve ./resources.yaml --storage dynamodb --connection http://localhost:4566   # floci
          aep serve ./resources.yaml --storage postgres --set Storage:Postgres:MaxPoolSize=20 --set Storage:Postgres:SslMode=Require
        """);
}
