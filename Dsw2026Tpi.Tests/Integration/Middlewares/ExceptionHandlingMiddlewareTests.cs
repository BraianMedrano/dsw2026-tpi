using System.Text.Json;
using Dsw2026Tpi.Api.Middlewares;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dsw2026Tpi.Tests.Integration.Middlewares;

public class ExceptionHandlingMiddlewareTests
{
    [Theory]
    [MemberData(nameof(KnownExceptions))]
    public async Task InvokeAsync_MapsKnownExceptionsToTheirHttpStatus(
        Exception exception,
        int expectedStatus)
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(exception);

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsCamelCaseErrorContract()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(
            new ValidationException().WithDetail("email", "required"));

        await middleware.InvokeAsync(context);

        using var document = await ReadResponse(context);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("errorCode", out _));
        Assert.True(root.TryGetProperty("message", out _));
        Assert.True(root.TryGetProperty("details", out var details));
        Assert.Equal(JsonValueKind.Array, details.ValueKind);
        var detail = Assert.Single(details.EnumerateArray());
        Assert.Equal("email", detail.GetProperty("field").GetString());
        Assert.Equal("required", detail.GetProperty("issue").GetString());
        Assert.False(root.TryGetProperty("ErrorCode", out _));
    }

    [Fact]
    public async Task InvokeAsync_HidesUnexpectedExceptionDetails()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(new InvalidOperationException("sensitive database detail"));

        await middleware.InvokeAsync(context);

        using var document = await ReadResponse(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain(
            "sensitive database detail",
            document.RootElement.GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<Exception, int> KnownExceptions() =>
        new()
        {
            { new ValidationException(), StatusCodes.Status400BadRequest },
            { new EntityNotFoundException("Doctor"), StatusCodes.Status404NotFound },
            { new ConflictException("RESOURCE_CONFLICT", "The resource already exists."), StatusCodes.Status409Conflict },
            { new AuthenticationException(), StatusCodes.Status401Unauthorized },
            { new AuthorizationException(), StatusCodes.Status403Forbidden },
            { new ServiceUnavailableException("DEPENDENCY_UNAVAILABLE", "Dependency unavailable."), StatusCodes.Status503ServiceUnavailable }
        };

    [Fact]
    public async Task InvokeAsync_PreservesConflictErrorCodeAndMessage()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware(
            new ConflictException("RESOURCE_CONFLICT", "The resource already exists."));

        await middleware.InvokeAsync(context);

        using var document = await ReadResponse(context);
        Assert.Equal(
            "RESOURCE_CONFLICT",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            "The resource already exists.",
            document.RootElement.GetProperty("message").GetString());
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ExceptionHandlingMiddleware CreateMiddleware(Exception exception) =>
        new(
            _ => Task.FromException(exception),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

    private static async Task<JsonDocument> ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
