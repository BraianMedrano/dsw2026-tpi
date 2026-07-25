using Dsw2026Tpi.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Tests.Unit.Controllers;

public class ControllerRouteTests
{
    [Theory]
    [InlineData(typeof(AuthenticationController), "api/auth")]
    [InlineData(typeof(DoctorController), "api/doctors")]
    public void Controller_UsesRequiredApiPrefix(Type controllerType, string expectedTemplate)
    {
        var route = Assert.Single(
            controllerType.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
                .Cast<RouteAttribute>());

        Assert.Equal(expectedTemplate, route.Template);
    }

    [Fact]
    public void AppController_DoesNotDeclareAnAlternativeRoute()
    {
        var routes = typeof(AppController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false);

        Assert.Empty(routes);
    }

    [Fact]
    public void AuthenticationController_DoesNotExposeAdministratorRegistration()
    {
        var publicPostRoutes = typeof(AuthenticationController)
            .GetMethods()
            .SelectMany(method => method
                .GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
                .Cast<HttpPostAttribute>())
            .Select(attribute => attribute.Template);

        Assert.DoesNotContain("admin/register", publicPostRoutes);
    }

    [Fact]
    public void AuthenticationController_ExposesOnlyTheTwoAnonymousLoginActions()
    {
        var anonymousPosts = typeof(AuthenticationController)
            .GetMethods()
            .Where(method => method.IsPublic)
            .Select(method => new
            {
                Method = method,
                Route = method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false)
                    .Cast<HttpPostAttribute>()
                    .SingleOrDefault()?.Template
            })
            .Where(item => item.Route is not null &&
                item.Method.IsDefined(typeof(AllowAnonymousAttribute), inherit: false))
            .Select(item => item.Route!)
            .Order()
            .ToArray();

        Assert.Equal(["admin/login", "patient/login"], anonymousPosts);
    }
}
