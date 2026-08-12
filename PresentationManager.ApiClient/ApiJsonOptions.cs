using System.Text.Json;

namespace PresentationManager.ApiClient;

/// <summary>PresentationManager.API serializes with ASP.NET Core's default camelCase output
/// ("id", "eventStartDate", ...). System.Text.Json is case-sensitive by default when reading, and its
/// System.Net.Http.Json helpers don't pick up ASP.NET Core's camelCase convention on their own - every
/// HTTP call in this project passes this explicitly so PascalCase C# record properties still round-trip
/// against the API's camelCase wire format.</summary>
internal static class ApiJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
