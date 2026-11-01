using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;

namespace SE.Roleplay.Framework.Directory.Services;

public static class OwnerRequestSecurity
{
    public static string? GetVerifiedSteamId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return value is { Length: 17 } && value.All(character => character is >= '0' and <= '9')
            ? value : null;
    }

    public static async Task<bool> ValidateMutationAsync(
        HttpContext context, IAntiforgery antiforgery, Uri? configuredPublicOrigin)
    {
        var origins = context.Request.Headers.Origin;
        if (configuredPublicOrigin is null || origins.Count != 1 ||
            !string.Equals(origins[0], configuredPublicOrigin.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
