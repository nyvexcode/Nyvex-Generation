using System.Text.Json.Serialization;

namespace NyvexGeneration.Models;

// Modelos do endpoint POST https://users.roblox.com/v1/usernames/users
// (Roblox.Users.Api.MultiGetByUsernameRequest / MultiGetUserByNameResponse).

public sealed class UsernameLookupRequestDto
{
    [JsonPropertyName("usernames")]
    public string[] Usernames { get; set; } = Array.Empty<string>();

    [JsonPropertyName("excludeBannedUsers")]
    public bool ExcludeBannedUsers { get; set; }
}

public sealed class UsernameLookupResponseDto
{
    [JsonPropertyName("data")]
    public List<UsernameLookupItemDto?>? Data { get; set; }
}

public sealed class UsernameLookupItemDto
{
    [JsonPropertyName("requestedUsername")]
    public string? RequestedUsername { get; set; }

    [JsonPropertyName("hasVerifiedBadge")]
    public bool HasVerifiedBadge { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}
