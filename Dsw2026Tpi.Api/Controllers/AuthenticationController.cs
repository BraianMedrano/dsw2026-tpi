using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;

namespace Dsw2026Tpi.Api.Controllers;

[Route("api/auth")]
public class AuthenticationController : AppController
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ITokenRevocationService _tokenRevocationService;

    public AuthenticationController(
        IAuthenticationService authenticationService,
        ITokenRevocationService tokenRevocationService)
    {
        _authenticationService = authenticationService;
        _tokenRevocationService = tokenRevocationService;
    }

    [HttpPost("admin/login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginAdmin([FromBody] LoginAdminModel.Request request)
    {
        var result = await _authenticationService.LoginAdmin(request);
        return Ok(result);
    }

    [HttpPost("patient/login")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginPatient([FromBody] LoginPatientModel.Request request)
    {
        var result = await _authenticationService.LoginPatient(request);
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expirationValue = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (string.IsNullOrWhiteSpace(jti) ||
            !long.TryParse(
                expirationValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var expirationSeconds))
        {
            throw new AuthenticationException();
        }

        DateTime expiresAtUtc;
        try
        {
            expiresAtUtc = DateTimeOffset
                .FromUnixTimeSeconds(expirationSeconds)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new AuthenticationException();
        }

        await _tokenRevocationService.RevokeAsync(jti, expiresAtUtc);
        return Ok("ok");
    }
}
