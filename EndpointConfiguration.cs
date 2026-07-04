/// <summary>
/// Optional additional endpoint configuration
/// {
///     "summary": "A short description",
///     "description": "A longer description",
///     "queryParams": [
///         { 
///             "name": "param0",
///             "type": "string",
///             "required": false,
///             "description": "an example"
///         }
///     ]
/// }
/// </summary>
class EndpointConfiguration
{
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public List<QueryParameter> QueryParams { get; set; } = [];

}

class QueryParameter
{
    public string? Name { get; set; }
    public string? Type { get; set; } // string, boolean, number ... any others? probably not object ...
    public bool? Required { get; set; }
    public string Description { get; set; } = "";
}