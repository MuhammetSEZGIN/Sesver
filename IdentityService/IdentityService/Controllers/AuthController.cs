using System.Net;
using System.Security.Claims;
using IdentityService.Attributes;
using IdentityService.DTOs;
using IdentityService.Examples;
using IdentityService.Interfaces;
using IdentityService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace IdentityService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IEmailService _emailService;
        private readonly IRegisterService _registerService;
        private readonly IConfiguration _configuration;

        public AuthController(
            IAuthService authService,
            IEmailService emailService,
            IRegisterService registerService,
            IConfiguration configuration
        )
        {
            _authService = authService;
            _emailService = emailService;
            _registerService = registerService;
            _configuration = configuration;
        }

        /// <summary>
        /// Registers a new user.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///     POST /Todo
        ///     {
        ///        "id": 1,
        ///        "name": "Item #1",
        ///        "isComplete": true
        ///     }
        /// </remarks>
        /// <param name="model">The registration model containing user details.</param>
        [HttpPost("register")]
        [ValidateModel]
        [SwaggerRequestExample(typeof(RegisterModel), typeof(RegisterRequestExample))]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            var result = await _registerService.RegisterAsync(model);

            if (!result.IsSuccessfull)
            {
                return new ObjectResult(result) { StatusCode = result.StatusCode };
            }

            var confirmationUrl = GetEmailConfirmationUrl();

            if (result.IsSuccessfull)
            {
                var emailResult = await _emailService.SendEmailConfirmationAsync(
                    result.Data.UserId,
                    confirmationUrl
                );
            }

            return new ObjectResult(
                new RegisterResponseDto
                {
                    UserId = result.Data.UserId,
                    RefreshToken = result.Data.RefreshToken,
                    Token = result.Data.Token,
                }
            )
            {
                StatusCode = result.StatusCode,
            };
        }

        [HttpPost("login")]
        [ValidateModel]
        [SwaggerRequestExample(typeof(LoginRequestModel), typeof(LoginRequestExample))]
        public async Task<IActionResult> Login([FromBody] LoginRequestModel model)
        {
            var loginResult = await _authService.LoginAsync(model);
            return new ObjectResult(loginResult) { StatusCode = loginResult.StatusCode };
        }

        /// <summary>
        /// Sends a password reset link when the email belongs to an account.
        /// The response is intentionally identical for known and unknown addresses.
        /// </summary>
        [HttpPost("forgot-password")]
        [ValidateModel]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto model)
        {
            await _emailService.SendPasswordResetAsync(model.Email);

            var response = ApiResponse<object>.Success(
                "If an account exists for this email, a password reset link has been sent.",
                (int)HttpStatusCode.OK
            );
            return new ObjectResult(response) { StatusCode = response.StatusCode };
        }

        /// <summary>
        /// Resets a password with the one-time token delivered by email.
        /// </summary>
        [HttpPost("reset-password")]
        [ValidateModel]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto model)
        {
            var result = await _emailService.ResetPasswordAsync(model);
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto model)
        {
            if (string.IsNullOrEmpty(model.RefreshToken) || string.IsNullOrEmpty(model.UserId))
            {
                return new ObjectResult(
                    ApiResponse<string>.Failed("Refresh token or User ID cannot be empty")
                )
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                };
            }

            var result = await _authService.RefreshTokenAsync(model);
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [Authorize]
        [HttpGet("my-sessions")]
        public async Task<IActionResult> GetMySessions()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _authService.GetMySessionsByUserId(userId);

            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [Authorize]
        [HttpPost("logout-session/{sessionId}")]
        public async Task<IActionResult> LogoutSession(string sessionId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _authService.LogoutSessionAsync(sessionId, userId);
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpPost("send-email-test")]
        public async Task<IActionResult> SendEmailTest([FromBody] EmailRequestDto model)
        {
            var result = await _emailService.SendEmailAsync(
                model.ToEmail,
                model.Subject,
                model.Content
            );
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpGet("confirm-email")]
        public async Task<IActionResult> ConfirmEmail(
            [FromQuery] string userId,
            [FromQuery] string token
        )
        {
            var result = await _emailService.ConfirmEmail(userId, token);
            return new ObjectResult(result.Data) { StatusCode = result.StatusCode };
        }

        [HttpPost("resend-confirmation-email")]
        public async Task<IActionResult> ResendConfirmationEmail([FromBody] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("User ID is required.");
            }
            var confirmationUrl = GetEmailConfirmationUrl();

            var emailResult = await _emailService.SendEmailConfirmationAsync(
                userId,
                confirmationUrl
            );
            if (!emailResult.IsSuccessfull)
            {
                return new ObjectResult(emailResult) { StatusCode = emailResult.StatusCode };
            }
            return new ObjectResult(emailResult) { StatusCode = emailResult.StatusCode };
        }

        private string GetEmailConfirmationUrl()
        {
            var configuredUrl = _configuration["ClientApp:EmailConfirmationUrl"];
            return string.IsNullOrWhiteSpace(configuredUrl)
                ? $"{Request.Scheme}://{Request.Host}/api/Auth/confirm-email"
                : configuredUrl;
        }
    }
}
