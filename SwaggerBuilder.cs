using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;
using Newtonsoft.Json;

static class SwaggerBuilder
{
    static string serviceName = "";
    static string serviceVersion = "";
    static string serviceDescription = "";
    static string baseFolder = "";
    static string inputFolder = "";
    static string outputFolder = "";
    static string baseUrl = "";
    static string authScheme = "";

    static void Configure(IConfigurationBuilder builder)
    {
        IConfiguration configuration = builder.Build();

        serviceName = configuration["AppSettings:ServiceName"] ?? "SOA API";
        serviceVersion = configuration["AppSettings:ServiceVersion"] ?? "1.0.0";
        serviceDescription = "[swagger.json](swagger.json)\n\n" + configuration["AppSettings:ServiceDescription"] ?? "";
        baseFolder = configuration["AppSettings:BaseFolder"] ?? throw new InvalidOperationException("Base folder is required");
        inputFolder = Path.Combine(baseFolder, "Input");
        outputFolder = Path.Combine(baseFolder, "Output");
        baseUrl = configuration["AppSettings:BaseUrl"] ?? "https://localhost";
        authScheme = configuration["AppSettings:Authentication"] ?? "Bearer"; // Bearer is standard but this should also work for Basic
    }

    internal static async Task GenerateDocument(IConfigurationBuilder builder)
    {
        Configure(builder);

        if (!Directory.Exists(inputFolder))
        {
            Directory.CreateDirectory(inputFolder);
            // if the file was just created, it's going to be empty
            Console.WriteLine($"Please place JSON file pairs inside this folder: {inputFolder}");
            return;
        }
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder); // files will be created here
        }

        // the generated swagger file
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = serviceName,
                Version = serviceVersion,
                Description = serviceDescription
            },
            Paths = [],
            Components = new OpenApiComponents(),
            Servers =
            [
                new OpenApiServer  { Url = baseUrl, Description = "Production"}
            ]
        };

        if (document.Components.Schemas == null)
        {
            document.Components.Schemas = new Dictionary<string, IOpenApiSchema>();
        }

        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        var bearerScheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = authScheme.ToLower(),
            BearerFormat = "JWT", // Optional: informs the user it expects a JWT token layout
            Description = $"Enter your {(authScheme == "Bearer" ? "JWT Bearer token" : "user name and password")} to authenticate requests."
        };

        string schemeKey = $"{authScheme}Auth";
        if (!document.Components.SecuritySchemes.ContainsKey(schemeKey))
        {
            document.Components.SecuritySchemes.Add(schemeKey, bearerScheme);
        }

        var securityRequirement = new OpenApiSecurityRequirement
        {
            {
                // Reference the scheme we just added above
                new OpenApiSecuritySchemeReference(schemeKey),
                new List<string>() // Empty list signifies no specific OAuth scopes are required
            }
        };

        // Apply the security requirement globally to the whole document
        document.Security ??= [];
        document.Security.Add(securityRequirement);

        // everything hinges on the input json, meaning if you want an endpoint with no input, you have to add a file containing the word null
        // (this could be changed fairly easily to search for one or the other but it's not very useful)
        // TODO: At the moment, this means GET need a file named GET foo.input.jon with null for body. Better to change that
        var inputFiles = Directory.EnumerateFiles(inputFolder, "*.input.json", SearchOption.AllDirectories);

        foreach (var inputFilePath in inputFiles.OrderBy(o => o))
        {
            await AddEndpointToDocument(document, inputFilePath);
        }

        string openApiJson = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0);

        await File.WriteAllTextAsync(Path.Combine(outputFolder, "swagger.json"), openApiJson);

        string htmlOutput = GetSwaggerUiHtml(openApiJson, document.Info.Title);
        string outputPath = Path.Combine(outputFolder, "api-docs.html");

        await File.WriteAllTextAsync(outputPath, htmlOutput);

        Console.WriteLine($"\nSuccess! Combined documentation webpage saved to:\n{outputPath}");
    }

    static async Task AddEndpointToDocument(OpenApiDocument document, string inputFilePath)
    {
        // get the API route from the relative directory nesting structure
        // Example: "api/customer/order/update.input.json" -> "customer/order/update"
        string relativePath = Path.GetRelativePath(inputFolder, inputFilePath);
        string apiRoute = relativePath.Replace(".input.json", "").Replace(Path.DirectorySeparatorChar, '/');
        Console.WriteLine($"Processing: {apiRoute}");

        // If filename has a space in it, the word before the space should be a HTTP Verb. EX "GET url.input.json" (both could exist).
        // If no filename (just url.input.json), it is assumed to be a post.
        // Note: Most servers do not accept certain verbs with bodies, but this program ties everything to an ".input.json" file.
        // In the future, this will base the main loop on ANY ".something.json" file, but for now just add an empty file.

        // Look for a space. If found, overwrite apiRoute and httpVerb. If multiple spaces, invalid setup.
        HttpMethod httpVerb = HttpMethod.Post;
        if (apiRoute.Contains(' '))
        {
            var directoryPath = Path.GetDirectoryName(apiRoute);
            var finalFile = Path.GetFileNameWithoutExtension(apiRoute);

            var splitUp = finalFile.Split([" "], 2, StringSplitOptions.RemoveEmptyEntries);
            httpVerb = splitUp[0].ToLower() switch
            {
                "get" => HttpMethod.Get,
                "post" => HttpMethod.Post,
                "put" => HttpMethod.Put,
                "patch" => HttpMethod.Patch,
                "delete" => HttpMethod.Delete,
                _ => HttpMethod.Post
            };
            finalFile = splitUp[1];
            apiRoute = directoryPath + "/" + finalFile;
        }

        // Spaces aren't allowed in URLs unless escaped
        if (apiRoute.Contains(' '))
        {
            throw new InvalidOperationException($"{inputFilePath} has an invalid URI");
        }

        var config = await ReadConfiguration(inputFilePath.Replace(".input.json", ".config.json"));

        // get a clean, human-readable tag/category from the top-level directory folder name
        string[] routeSegments = apiRoute.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var controllerTag = new OpenApiTagReference(SanitizeTagName(routeSegments.Length > 1 ? routeSegments[0] : "General"));

        var operation = new OpenApiOperation
        {
            Summary = config?.Summary ?? $"Execute [{apiRoute}]",
            Description = config?.Description ?? "Executes the selected endpoint",
            Tags = new HashSet<OpenApiTagReference>
            {
                controllerTag,
            },
        };

        // very simple assumption, {this} only appears if there is a param
        foreach (var routeParam in routeSegments.Where(w => w.StartsWith('{') && w.EndsWith('}')))
        {
            config ??= new EndpointConfiguration();
            config.QueryParams ??= [];
            if (!config.QueryParams.Any(a => a.Name == routeParam))
            {
                config.QueryParams.Add(new QueryParameter
                {
                    Name = routeParam,
                    Description = "Route parameter"
                });
            }
        }

        await AddEndpointRequestToDocument(document, inputFilePath, apiRoute, operation, httpVerb, config);
        await AddEndpointResponseToDocument(document, inputFilePath.Replace(".input.json", ".output.json"), apiRoute, operation, httpVerb);

        string cleanRoute = $"/{apiRoute.TrimStart('/')}";
        if (!document.Paths.TryGetValue(cleanRoute, out var pathItem))
        {
            pathItem = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [httpVerb] = operation
                }
            };
            document.Paths.Add(cleanRoute, pathItem);
        }
        if (pathItem.Operations!.ContainsKey(httpVerb))
        {
            // If it already exists from another process, remove it first
            pathItem.Operations.Remove(httpVerb);
        }

        pathItem.Operations.Add(httpVerb, operation);

        static string SanitizeTagName(string route)
        {
            if (string.IsNullOrEmpty(route)) return "Default";
            // Grabs the first segment of the path to use as a clean UI category group
            string firstSegment = route.Split('/')[0];
            return char.ToUpper(firstSegment[0]) + firstSegment.Substring(1);
        }
    }

    static async Task AddEndpointRequestToDocument(OpenApiDocument document, string filePath, string apiRoute, OpenApiOperation operation, HttpMethod httpVerb, EndpointConfiguration? config)
    {
        foreach (var param in config?.QueryParams ?? [])
        {
            operation.Parameters ??= [];

            // Add a query parameter for each row
            JsonSchemaType parameterType = param.Type?.ToLower() switch
            {
                "string" => JsonSchemaType.String,
                "integer" => JsonSchemaType.Integer,
                "number" => JsonSchemaType.Number,
                "boolean" => JsonSchemaType.Boolean,
                "array" => JsonSchemaType.Array,
                _ => JsonSchemaType.String // Safe fallback
            };

            var isPathParam = param.Name?.StartsWith('{') == true && param.Name.EndsWith('}');

            var openApiParam = new OpenApiParameter
            {
                Name = param.Name,
                Description = param.Description,
                In = isPathParam ? ParameterLocation.Path : ParameterLocation.Query,
                Schema = new OpenApiSchema
                {
                    Type = parameterType
                },
                Required = param.Required == true || isPathParam // path must be required
            };

            operation.Parameters.Add(openApiParam);
        }

        string jsonContent = await File.ReadAllTextAsync(filePath);
        if (httpVerb != HttpMethod.Get && httpVerb != HttpMethod.Delete && !string.IsNullOrWhiteSpace(jsonContent) && jsonContent != "null")
        {
            string schemaName = $"Request_{httpVerb}_{SanitizeSchemaName(apiRoute)}";

            using var jsonDoc = JsonDocument.Parse(jsonContent);

            // Convert the structural payload recursively, feeding it the global Components vault
            var mainSchema = ConvertJsonElementToSchema(jsonDoc.RootElement, document.Components!, schemaName);

            // Safeguard the global schema table and attach the top-level schema to components
            if (!document.Components!.Schemas!.ContainsKey(schemaName))
            {
                document.Components.Schemas.Add(schemaName, mainSchema);
            }

            var bodySchema = new OpenApiSchemaReference($"#/components/schemas/{schemaName}");

            // Initialize the whole RequestBody object inline, including the inner Content map
            operation.RequestBody = new OpenApiRequestBody
            {
                Description = "Input Payload Content",
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = bodySchema
                    }
                }
            };
        }
    }

    static async Task AddEndpointResponseToDocument(OpenApiDocument document, string filePath, string apiRoute, OpenApiOperation operation, HttpMethod httpVerb)
    {
        string? jsonContent = null;
        if (File.Exists(filePath))
        {
            jsonContent = await File.ReadAllTextAsync(filePath);
        }

        string schemaName = $"Response_{httpVerb}_{SanitizeSchemaName(apiRoute)}";

        var response = new OpenApiResponse { Description = "Successful execution" };

        if (!String.IsNullOrWhiteSpace(jsonContent) && jsonContent != "null")
        {
            using var jsonDoc = JsonDocument.Parse(jsonContent);

            // Convert the structural payload recursively, feeding it the global Components vault
            var mainSchema = ConvertJsonElementToSchema(jsonDoc.RootElement, document.Components!, schemaName);

            // Safeguard the global schema table and attach the top-level schema to components
            if (!document.Components!.Schemas!.ContainsKey(schemaName))
            {
                document.Components.Schemas.Add(schemaName, mainSchema);
            }

            var bodySchema = new OpenApiSchemaReference($"#/components/schemas/{schemaName}");

            response.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = bodySchema
                }
            };
        }

        operation.Responses!.Add("200", response);
    }

    /// <summary>
    /// Recursively analyzes JSON types and structural definitions. 
    /// Extracts complex structural nodes straight into the global schema definition area to avoid path corruption.
    /// </summary>
    private static OpenApiSchema ConvertJsonElementToSchema(JsonElement element, OpenApiComponents components, string currentContextName)
    {
        var schema = new OpenApiSchema();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                schema.Type = JsonSchemaType.Object;
                schema.Properties = new Dictionary<string, IOpenApiSchema>();
                foreach (var property in element.EnumerateObject())
                {
                    string propertyName = property.Name;
                    JsonElement propertyValue = property.Value;

                    if (propertyValue.ValueKind == JsonValueKind.Object)
                    {
                        string subSchemaName = $"{currentContextName}_{SanitizeSchemaName(propertyName)}";

                        if (components.Schemas == null)
                        {
                            components.Schemas = new Dictionary<string, IOpenApiSchema>();
                        }

                        if (!components.Schemas.ContainsKey(subSchemaName))
                        {
                            var subSchema = ConvertJsonElementToSchema(propertyValue, components, subSchemaName);
                            components.Schemas.Add(subSchemaName, subSchema);
                        }

                        schema.Properties.Add(propertyName, new OpenApiSchemaReference($"#/components/schemas/{subSchemaName}"));
                    }
                    else if (propertyValue.ValueKind == JsonValueKind.Array)
                    {
                        schema.Properties.Add(propertyName, ConvertJsonElementToSchema(propertyValue, components, $"{currentContextName}_{SanitizeSchemaName(propertyName)}"));
                    }
                    else
                    {
                        schema.Properties.Add(propertyName, ConvertJsonElementToSchema(propertyValue, components, currentContextName));
                    }
                }
                break;

            case JsonValueKind.Array:
                schema.Type = JsonSchemaType.Array;

                if (element.GetArrayLength() > 0)
                {
                    // Enumerate the array elements and grab the first item to sample its type
                    using var enumerator = element.EnumerateArray();
                    enumerator.MoveNext();
                    JsonElement firstItem = enumerator.Current;

                    if (firstItem.ValueKind == JsonValueKind.Object)
                    {
                        string itemSchemaName = $"{currentContextName}Item";

                        if (components.Schemas == null)
                        {
                            components.Schemas = new Dictionary<string, IOpenApiSchema>();
                        }

                        if (!components.Schemas.ContainsKey(itemSchemaName))
                        {
                            var itemSchema = ConvertJsonElementToSchema(firstItem, components, itemSchemaName);
                            components.Schemas.Add(itemSchemaName, itemSchema);
                        }

                        // FIX: Directly assign the reference object to Items
                        schema.Items = new OpenApiSchemaReference($"#/components/schemas/{itemSchemaName}");
                    }
                    else
                    {
                        schema.Items = ConvertJsonElementToSchema(firstItem, components, currentContextName);
                    }
                }
                else
                {
                    schema.Items = new OpenApiSchema { Type = JsonSchemaType.String };
                }
                break;

            case JsonValueKind.String:
                schema.Type = JsonSchemaType.String;
                break;

            case JsonValueKind.Number:
                schema.Type = JsonSchemaType.Number;
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                schema.Type = JsonSchemaType.Boolean;
                break;

            case JsonValueKind.Null:
            default:
                schema.Type = JsonSchemaType.String | JsonSchemaType.Null;
                break;
        }

        return schema;
    }

    static string SanitizeSchemaName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "Object";
        string cleaned = input.Replace("/", "_").Replace("-", "_").Replace(" ", "_").Replace("{", "_").Replace("}", "_");
        return char.ToUpper(cleaned[0]) + cleaned.Substring(1);
    }

    static async Task<EndpointConfiguration?> ReadConfiguration(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }
        var content = await File.ReadAllTextAsync(filePath);
        return JsonConvert.DeserializeObject<EndpointConfiguration>(content);
    }

    static string GetSwaggerUiHtml(string openApiJson, string title)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>{title}</title>
    <link rel=""stylesheet"" href=""https://unpkg.com/swagger-ui-dist@5.32.8/swagger-ui.css"" />
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5.32.8/swagger-ui-bundle.js"" crossorigin></script>
    <script src=""https://unpkg.com/swagger-ui-dist@5.32.8/swagger-ui-standalone-preset.js""></script>
    <script>
    window.onload = function() {{
        window.ui = SwaggerUIBundle({{
            spec: {openApiJson},
            dom_id: '#swagger-ui',
            deepLinking: true,
            presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
            layout: ""BaseLayout""
        }});
    }};
    </script>
</body>
</html>";
    }
}
