using System.Security.Claims;

namespace SafeSpend.Web.Services.Identity;

public interface ICurrentUserContext
{
    string GetRequiredUserId();
}

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserContext
{
    public string GetRequiredUserId()
    {
        var userId = httpContextAccessor.HttpContext?.User
            .FindFirstValue(ClaimTypes.NameIdentifier);

        return !string.IsNullOrWhiteSpace(userId)
            ? userId
            : throw new InvalidOperationException(
                "An authenticated user is required.");
    }
}
