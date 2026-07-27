using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace IdentityService.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIpAddressService _ipAddressService;
    private readonly IRefreshTokenService _refreshTokenService;

    public EmailService(
        IConfiguration configuration,
        ILogger<EmailService> logger,
        UserManager<ApplicationUser> userManager,
        IIpAddressService ipAddressService,
        IRefreshTokenService refreshTokenService
    )
    {
        _configuration = configuration;
        _logger = logger;
        _userManager = userManager;
        _ipAddressService = ipAddressService;
        _refreshTokenService = refreshTokenService;
    }

    public async Task<ApiResponse<object>> SendEmailAsync(
        string toEmail,
        string subject,
        string content
    )
    {
        try
        {
            // appsettings veya User Secrets'tan SMTP ayarlarını çek
            var smtpSettings = _configuration.GetSection("Smtp");
            var isEnabled = smtpSettings.GetValue<bool>("Enabled", true);
            
            // Eğer mail servisi devre dışı ise, işlem yapmadan başarılı response döndür
            if (!isEnabled)
            {
                _logger.LogInformation("Mail servisi devre dışı. E-posta gönderilmedi: {Email}", toEmail);
                return ApiResponse<object>.Success(
                    "E-posta servisi devre dışı.",
                    (int)HttpStatusCode.OK
                );
            }
            var fromAddress = smtpSettings["FromAddress"];
            var fromName = smtpSettings["FromName"];
            var host = smtpSettings["Host"];
            if (!int.TryParse(smtpSettings["Port"], out var port))
            {
                throw new InvalidOperationException("Smtp:Port must be a valid number.");
            }
            var username = smtpSettings["Username"];
            var password = smtpSettings["Password"];

            if (
                string.IsNullOrWhiteSpace(fromAddress)
                || string.IsNullOrWhiteSpace(host)
                || string.IsNullOrWhiteSpace(username)
                || string.IsNullOrWhiteSpace(password)
            )
            {
                throw new InvalidOperationException("SMTP configuration is incomplete.");
            }

            using (var email = new MailMessage())
            using (var smtp = new SmtpClient(host, port))
            {
                email.From = new MailAddress(fromAddress, fromName ?? string.Empty);
                email.To.Add(new MailAddress(toEmail));
                email.Subject = subject;
                email.Body = content;
                email.IsBodyHtml = true;

                smtp.Credentials = new NetworkCredential(username, password);
                smtp.EnableSsl = true;
                await smtp.SendMailAsync(email);
            }

            _logger.LogInformation("SMTP ile e-posta başarıyla gönderildi: {Email}", toEmail);
            return ApiResponse<object>.Success(
                "E-posta başarıyla gönderildi.",
                (int)HttpStatusCode.OK
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SMTP ile e-posta gönderilirken bir hata oluştu. Alıcı: {Email}",
                toEmail
            );
            return ApiResponse<object>.Failed(
                "E-posta gönderilirken bir hata oluştu.",
                null,
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<object>> ConfirmEmail(string userId, string token)
    {
        try
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("User ID or token is null or empty");
                return ApiResponse<object>.Failed("User ID or token is null or empty");
            }
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found with ID: {0}", userId);
                return ApiResponse<object>.Failed("User not found");
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("Email already confirmed for user: {0}", user.UserName);
                return ApiResponse<object>.Success(
                    "Email already confirmed.",
                    (int)HttpStatusCode.OK
                );
            }
            string decodedToken;
            try
            {
                decodedToken = token.StartsWith("v2.", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token[3..]))
                    : token;
            }
            catch (FormatException)
            {
                return ApiResponse<object>.Failed("Email confirmation link is invalid.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Email confirmation failed for user: {0}", user.UserName);
                return ApiResponse<object>.Failed(
                    "Email confirmation failed.",
                    result.Errors.Select(e => e.Description)
                );
            }
            _logger.LogInformation("Email confirmed for user: {0}", user.UserName);
            var refreshTokenResult = await _refreshTokenService.CreateUserRefreshTokenAsync(
                user.Id,
                "Email Confirmation Device", // Default device info
                _ipAddressService.GetClientIpAddress()
            );
            if (!refreshTokenResult.IsSuccessfull)
            {
                _logger.LogWarning("Failed to create refresh token after email confirmation for user: {0}", user.UserName);
                return ApiResponse<object>.Failed(
                    "Email confirmed but failed to create session tokens.",
                    refreshTokenResult.Errors,
                    refreshTokenResult.StatusCode
                );
            }
            return ApiResponse<object>.Success(
                new { AccessToken = refreshTokenResult.Data.AccessToken, RefreshToken = refreshTokenResult.Data.RefreshToken },
                "Email confirmed successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while confirming email for user ID: {UserId}",
                userId
            );
            return ApiResponse<object>.Failed(
                "An error occurred while confirming email.",
                new List<string> { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<object>> SendEmailConfirmationAsync(
        string userId,
        string confirmationUrl
    )
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found with ID: {0}", userId);
                return ApiResponse<object>.Failed("User not found");
            }

            if (user.EmailConfirmed)
            {
                _logger.LogInformation("Email already confirmed for user: {0}", user.UserName);
                return ApiResponse<object>.Success(
                    "Email already confirmed.",
                    (int)HttpStatusCode.OK
                );
            }

            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = $"v2.{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))}";
            var fullConfirmationUrl = QueryHelpers.AddQueryString(
                confirmationUrl,
                new Dictionary<string, string>
                {
                    ["userId"] = userId,
                    ["token"] = encodedToken,
                }
            );

            var subject = "Email Confirmation";
            var safeUserName = HtmlEncoder.Default.Encode(user.UserName ?? user.Email);
            var safeConfirmationUrl = HtmlEncoder.Default.Encode(fullConfirmationUrl);
            var htmlContent =
                $@"
            <h2>Welcome {safeUserName}!</h2>
            <p>Please confirm your email address by clicking the link below:</p>
            <a href='{safeConfirmationUrl}' style='background-color: #4CAF50; color: white; padding: 14px 20px; text-decoration: none; display: inline-block; border-radius: 4px;'>
                Confirm Email
            </a>
            <p>If the button doesn't work, copy and paste this link into your browser:</p>
            <p>{safeConfirmationUrl}</p>
            <p>This link will expire in 6 hours.</p>
        ";

            var emailResult = await SendEmailAsync(user.Email, subject, htmlContent);
            if (emailResult.IsSuccessfull)
            {
                _logger.LogInformation(
                    "Email confirmation sent successfully to: {Email}",
                    user.Email
                );
                return ApiResponse<object>.Success(
                    "Confirmation email sent successfully. Please check your inbox.",
                    (int)HttpStatusCode.OK
                );
            }
            else
            {
                _logger.LogError("Failed to send confirmation email to: {Email}", user.Email);
                return ApiResponse<object>.Failed(
                    "Failed to send confirmation email. Please try again later.",
                    emailResult.Errors,
                    (int)HttpStatusCode.InternalServerError
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email confirmation to user: ");
            return ApiResponse<object>.Failed(
                "Failed to send confirmation email.",
                new[] { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<object>> SendPasswordResetAsync(string email)
    {
        const string genericMessage =
            "If an account exists for this email, a password reset link has been sent.";

        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogInformation(
                    "Password reset requested for an address that does not belong to an account."
                );
                return ApiResponse<object>.Success(genericMessage, (int)HttpStatusCode.OK);
            }

            var resetPasswordUrl = _configuration["ClientApp:PasswordResetUrl"];
            if (
                !Uri.TryCreate(resetPasswordUrl, UriKind.Absolute, out var parsedResetUrl)
                || (parsedResetUrl.Scheme != Uri.UriSchemeHttp
                    && parsedResetUrl.Scheme != Uri.UriSchemeHttps)
            )
            {
                _logger.LogError("ClientApp:PasswordResetUrl is missing or invalid.");
                return ApiResponse<object>.Failed(
                    "Password reset email could not be sent.",
                    null,
                    (int)HttpStatusCode.InternalServerError
                );
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var fullResetUrl = QueryHelpers.AddQueryString(
                parsedResetUrl.ToString(),
                new Dictionary<string, string>
                {
                    ["email"] = user.Email,
                    ["token"] = encodedToken,
                }
            );

            var safeUserName = HtmlEncoder.Default.Encode(user.UserName ?? user.Email);
            var safeResetUrl = HtmlEncoder.Default.Encode(fullResetUrl);
            var htmlContent = $@"
                <h2>Hello {safeUserName},</h2>
                <p>We received a request to reset your Sesver password.</p>
                <p><a href='{safeResetUrl}' style='background-color: #4CAF50; color: white; padding: 14px 20px; text-decoration: none; display: inline-block; border-radius: 4px;'>Reset Password</a></p>
                <p>If the button does not work, copy and paste this link into your browser:</p>
                <p>{safeResetUrl}</p>
                <p>This one-time link expires in 6 hours. If you did not request it, you can ignore this email.</p>";

            var emailResult = await SendEmailAsync(user.Email, "Reset your Sesver password", htmlContent);
            if (!emailResult.IsSuccessfull)
            {
                _logger.LogError("Password reset email could not be sent to {Email}.", user.Email);
                return emailResult;
            }

            return ApiResponse<object>.Success(genericMessage, (int)HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while preparing a password reset email.");
            return ApiResponse<object>.Failed(
                "Password reset email could not be sent.",
                null,
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequestDto model)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return InvalidResetRequest();
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(
                    WebEncoders.Base64UrlDecode(model.Token)
                );
            }
            catch (FormatException)
            {
                return InvalidResetRequest();
            }

            var result = await _userManager.ResetPasswordAsync(
                user,
                decodedToken,
                model.NewPassword
            );
            if (!result.Succeeded)
            {
                _logger.LogWarning("Password reset failed for user {UserId}.", user.Id);
                return InvalidResetRequest();
            }

            var invalidated = await _refreshTokenService.InvalidateAllUserSessionsAsync(user.Id);
            if (!invalidated)
            {
                _logger.LogCritical(
                    "Password reset succeeded but sessions could not be invalidated for user {UserId}.",
                    user.Id
                );
                return ApiResponse<object>.Failed(
                    "Password was reset, but active sessions could not be closed.",
                    null,
                    (int)HttpStatusCode.InternalServerError
                );
            }

            _logger.LogInformation("Password reset completed for user {UserId}.", user.Id);
            return ApiResponse<object>.Success(
                "Password has been reset successfully. Please sign in again.",
                (int)HttpStatusCode.OK
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while resetting a password.");
            return ApiResponse<object>.Failed(
                "An error occurred while resetting the password.",
                null,
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    private static ApiResponse<object> InvalidResetRequest() =>
        ApiResponse<object>.Failed(
            "The password reset link is invalid or has expired.",
            null,
            (int)HttpStatusCode.BadRequest
        );
}
