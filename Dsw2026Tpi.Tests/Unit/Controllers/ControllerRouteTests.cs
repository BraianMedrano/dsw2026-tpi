using Dsw2026Tpi.Api.Controllers;
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
}
