using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CRM.Data.Services;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CrmPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _permission;

    public CrmPermissionAttribute(string permission)
    {
        _permission = permission;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.IsInRole(CrmRoles.Administrador)) return;

        var idValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = user.FindFirstValue(ClaimTypes.Role);
        var allowed = int.TryParse(idValue, out var userId) &&
            await context.HttpContext.RequestServices.GetRequiredService<CrmPermissionService>()
                .HasAsync(userId, role, _permission);

        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
