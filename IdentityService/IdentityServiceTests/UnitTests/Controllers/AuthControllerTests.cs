using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using IdentityService.Controllers;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace IdentityServiceTests.UnitTests.Controllers
{
    public class AuthControllerTests
    {
        private readonly Mock<IAuthService> _mockAuthService;
        private readonly Mock<IEmailService> _mockEmailService;
        private readonly Mock<IRegisterService> _mockRegisterService;
        private readonly AuthController _controller;

        public AuthControllerTests()
        {
            _mockAuthService = new Mock<IAuthService>();
            _mockEmailService = new Mock<IEmailService>();
            _mockRegisterService = new Mock<IRegisterService>();

            _controller = new AuthController(
                _mockAuthService.Object,
                _mockEmailService.Object,
                _mockRegisterService.Object
            );

            // Setup HttpContext for URL generation
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost:5001");
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, TestUserId) },
                    "TestAuth"
                )
            );
            _controller.ControllerContext = new ControllerContext()
            {
                HttpContext = httpContext
            };
        }

        private const string TestUserId = "user123";

        #region Register Tests

        [Fact]
        public async Task Register_ValidModel_ReturnsSuccessResponse()
        {
            // Arrange
            var registerModel = new RegisterModel
            {
                UserName = "testuser",
                Email = "test@example.com",
                Password = "Test@123",
                PasswordConfirmation = "Test@123",
                DeviceInfo = "TestDevice"
            };

            var registerResponse = new RegisterResponseDto
            {
                UserId = "user123",
                Token = "access-token",
                RefreshToken = "refresh-token"
            };

            var apiResponse = ApiResponse<RegisterResponseDto>.Success(
                registerResponse,
                "Registration successful",
                (int)HttpStatusCode.Created
            );

            _mockRegisterService
                .Setup(x => x.RegisterAsync(It.IsAny<RegisterModel>()))
                .ReturnsAsync(apiResponse);

            _mockEmailService
                .Setup(x => x.SendEmailConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(ApiResponse<object>.Success("Email sent"));

            // Act
            var result = await _controller.Register(registerModel);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.Created, objectResult.StatusCode);
            var responseDto = Assert.IsType<RegisterResponseDto>(objectResult.Value);
            Assert.Equal("user123", responseDto.UserId);
            Assert.Equal("access-token", responseDto.Token);
            Assert.Equal("refresh-token", responseDto.RefreshToken);

            _mockRegisterService.Verify(x => x.RegisterAsync(registerModel), Times.Once);
            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync("user123", It.IsAny<string>()),
                Times.Once
            );
        }

        [Fact]
        public async Task Register_RegistrationFails_ReturnsErrorResponse()
        {
            // Arrange
            var registerModel = new RegisterModel
            {
                UserName = "testuser",
                Email = "test@example.com",
                Password = "Test@123",
                PasswordConfirmation = "Test@123"
            };

            var apiResponse = ApiResponse<RegisterResponseDto>.Failed(
                message: "Username already exists",
               statusCode: (int)HttpStatusCode.BadRequest
            );

            _mockRegisterService
                .Setup(x => x.RegisterAsync(It.IsAny<RegisterModel>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.Register(registerModel);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<RegisterResponseDto>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);
            Assert.Equal("Username already exists", response.Message);

            _mockRegisterService.Verify(x => x.RegisterAsync(registerModel), Times.Once);
            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never
            );
        }

        #endregion

        #region Login Tests

        [Fact]
        public async Task Login_ValidCredentials_ReturnsSuccessResponse()
        {
            // Arrange
            var loginModel = new LoginRequestModel
            {
                UserName = "testuser",
                Password = "Test@123",
                DeviceInfo = "TestDevice"
            };

            var authResponse = new AuthResponseDto
            {
                UserID = "user123",
                AccessToken = "access-token",
                RefreshToken = "refresh-token"
            };

            var apiResponse = ApiResponse<AuthResponseDto>.Success(
                authResponse,
                "Login successful",
                (int)HttpStatusCode.OK
            );

            _mockAuthService
                .Setup(x => x.LoginAsync(It.IsAny<LoginRequestModel>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.Login(loginModel);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<AuthResponseDto>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);
            Assert.Equal("user123", response.Data.UserID);

            _mockAuthService.Verify(x => x.LoginAsync(loginModel), Times.Once);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized()
        {
            // Arrange
            var loginModel = new LoginRequestModel
            {
                UserName = "testuser",
                Password = "WrongPassword"
            };

            var apiResponse = ApiResponse<AuthResponseDto>.Failed(
                message:"Invalid credentials",
                statusCode: (int)HttpStatusCode.Unauthorized
            );

            _mockAuthService
                .Setup(x => x.LoginAsync(It.IsAny<LoginRequestModel>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.Login(loginModel);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.Unauthorized, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<AuthResponseDto>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);
            Assert.Equal("Invalid credentials", response.Message);

            _mockAuthService.Verify(x => x.LoginAsync(loginModel), Times.Once);
        }

        #endregion

        #region RefreshToken Tests

        [Fact]
        public async Task RefreshToken_ValidToken_ReturnsNewTokens()
        {
            // Arrange
            var refreshTokenDto = new RefreshTokenDto
            {
                RefreshToken = "valid-refresh-token",
                UserId = "user123"
            };

            var tokenResult = new RefreshTokenResultDto
            {
                AccessToken = "new-access-token",
                RefreshToken = "new-refresh-token"
            };

            var apiResponse = ApiResponse<RefreshTokenResultDto>.Success(
                tokenResult,
                "Token refreshed",
                (int)HttpStatusCode.OK
            );

            _mockAuthService
                .Setup(x => x.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.RefreshToken(refreshTokenDto);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<RefreshTokenResultDto>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);
            Assert.Equal("new-access-token", response.Data.AccessToken);

            _mockAuthService.Verify(x => x.RefreshTokenAsync(refreshTokenDto), Times.Once);
        }

        [Fact]
        public async Task RefreshToken_EmptyRefreshToken_ReturnsBadRequest()
        {
            // Arrange
            var refreshTokenDto = new RefreshTokenDto
            {
                RefreshToken = "",
                UserId = "user123"
            };

            // Act
            var result = await _controller.RefreshToken(refreshTokenDto);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);
            Assert.Equal("Refresh token or User ID cannot be empty", response.Message);

            _mockAuthService.Verify(
                x => x.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()),
                Times.Never
            );
        }

        [Fact]
        public async Task RefreshToken_EmptyUserId_ReturnsBadRequest()
        {
            // Arrange
            var refreshTokenDto = new RefreshTokenDto
            {
                RefreshToken = "valid-token",
                UserId = ""
            };

            // Act
            var result = await _controller.RefreshToken(refreshTokenDto);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);

            _mockAuthService.Verify(
                x => x.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()),
                Times.Never
            );
        }

        [Fact]
        public async Task RefreshToken_InvalidToken_ReturnsUnauthorized()
        {
            // Arrange
            var refreshTokenDto = new RefreshTokenDto
            {
                RefreshToken = "invalid-token",
                UserId = "user123"
            };

            var apiResponse = ApiResponse<RefreshTokenResultDto>.Failed(
                message: "Invalid refresh token",
                statusCode: (int)HttpStatusCode.Unauthorized
            );

            _mockAuthService
                .Setup(x => x.RefreshTokenAsync(It.IsAny<RefreshTokenDto>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.RefreshToken(refreshTokenDto);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.Unauthorized, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<RefreshTokenResultDto>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);

            _mockAuthService.Verify(x => x.RefreshTokenAsync(refreshTokenDto), Times.Once);
        }

        #endregion

        #region GetMySessions Tests

        [Fact]
        public async Task GetMySessions_ValidRequest_ReturnsSessionsList()
        {
            // Arrange
            var sessions = new List<UserSessionsResultDto>
            {
                new UserSessionsResultDto
                {
                    Id = "session1",
                    DeviceInfo = "Device1",
                    CreatedAt = System.DateTime.UtcNow
                },
                new UserSessionsResultDto
                {
                    Id = "session2",
                    DeviceInfo = "Device2",
                    CreatedAt = System.DateTime.UtcNow
                }
            };

            var apiResponse = ApiResponse<List<UserSessionsResultDto>>.Success(
                sessions,
                "Sessions retrieved",
                (int)HttpStatusCode.OK
            );

            _mockAuthService
                .Setup(x => x.GetMySessionsByUserId(TestUserId))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.GetMySessions();

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<List<UserSessionsResultDto>>>(
                objectResult.Value
            );
            Assert.True(response.IsSuccessfull);
            Assert.Equal(2, response.Data.Count);

            _mockAuthService.Verify(
                x => x.GetMySessionsByUserId(TestUserId),
                Times.Once
            );
        }

        [Fact]
        public async Task GetMySessions_NoSessions_ReturnsEmptyList()
        {
            // Arrange
            var sessions = new List<UserSessionsResultDto>();

            var apiResponse = ApiResponse<List<UserSessionsResultDto>>.Success(
                sessions,
                "No sessions found",
                (int)HttpStatusCode.OK
            );

            _mockAuthService
                .Setup(x => x.GetMySessionsByUserId(TestUserId))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.GetMySessions();

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<List<UserSessionsResultDto>>>(
                objectResult.Value
            );
            Assert.True(response.IsSuccessfull);
            Assert.Empty(response.Data);

            _mockAuthService.Verify(
                x => x.GetMySessionsByUserId(TestUserId),
                Times.Once
            );
        }

        #endregion

        #region LogoutSession Tests

        [Fact]
        public async Task LogoutSession_ValidSessionId_ReturnsSuccess()
        {
            // Arrange
            var sessionId = "session123";
            var apiResponse = ApiResponse<string>.Success(
                "Logout successful",
                (int)HttpStatusCode.OK
            );

            _mockAuthService
                .Setup(x => x.LogoutSessionAsync(sessionId, TestUserId))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.LogoutSession(sessionId);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);

            _mockAuthService.Verify(x => x.LogoutSessionAsync(sessionId, TestUserId), Times.Once);
        }

        [Fact]
        public async Task LogoutSession_InvalidSessionId_ReturnsNotFound()
        {
            // Arrange
            var sessionId = "invalid-session";
            var apiResponse = ApiResponse<string>.Failed(
                message: "Session not found",
                statusCode: (int)HttpStatusCode.NotFound
            );

            _mockAuthService
                .Setup(x => x.LogoutSessionAsync(sessionId, TestUserId))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.LogoutSession(sessionId);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.NotFound, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<string>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);

            _mockAuthService.Verify(x => x.LogoutSessionAsync(sessionId, TestUserId), Times.Once);
        }

        #endregion

        #region SendEmailTest Tests

        [Fact]
        public async Task SendEmailTest_ValidRequest_ReturnsSuccess()
        {
            // Arrange
            var emailRequest = new EmailRequestDto
            {
                ToEmail = "test@example.com",
                Subject = "Test Subject",
                Content = "Test Content"
            };

            var apiResponse = ApiResponse<object>.Success(
                "Email sent successfully",
                (int)HttpStatusCode.OK
            );

            _mockEmailService
                .Setup(
                    x =>
                        x.SendEmailAsync(
                            emailRequest.ToEmail,
                            emailRequest.Subject,
                            emailRequest.Content
                        )
                )
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.SendEmailTest(emailRequest);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);

            _mockEmailService.Verify(
                x =>
                    x.SendEmailAsync(
                        emailRequest.ToEmail,
                        emailRequest.Subject,
                        emailRequest.Content
                    ),
                Times.Once
            );
        }

        [Fact]
        public async Task SendEmailTest_EmailServiceFails_ReturnsError()
        {
            // Arrange
            var emailRequest = new EmailRequestDto
            {
                ToEmail = "test@example.com",
                Subject = "Test Subject",
                Content = "Test Content"
            };

            var apiResponse = ApiResponse<object>.Failed(
                message: "Failed to send email",
                statusCode: (int)HttpStatusCode.InternalServerError
            );

            _mockEmailService
                .Setup(
                    x =>
                        x.SendEmailAsync(
                            emailRequest.ToEmail,
                            emailRequest.Subject,
                            emailRequest.Content
                        )
                )
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.SendEmailTest(emailRequest);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);

            _mockEmailService.Verify(
                x =>
                    x.SendEmailAsync(
                        emailRequest.ToEmail,
                        emailRequest.Subject,
                        emailRequest.Content
                    ),
                Times.Once
            );
        }

        #endregion

        #region ConfirmEmail Tests

        [Fact]
        public async Task ConfirmEmail_ValidTokenAndUserId_ReturnsSuccess()
        {
            // Arrange
            var userId = "user123";
            var token = "valid-token";
            var apiResponse = ApiResponse<object>.Success(
                "Email confirmed successfully",
                (int)HttpStatusCode.OK
            );

            _mockEmailService
                .Setup(x => x.ConfirmEmail(userId, token))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.ConfirmEmail(userId, token);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);

            _mockEmailService.Verify(x => x.ConfirmEmail(userId, token), Times.Once);
        }

        [Fact]
        public async Task ConfirmEmail_InvalidToken_ReturnsError()
        {
            // Arrange
            var userId = "user123";
            var token = "invalid-token";
            var apiResponse = ApiResponse<object>.Failed(
                message: "Invalid token",
                statusCode: (int)HttpStatusCode.BadRequest
            );

            _mockEmailService
                .Setup(x => x.ConfirmEmail(userId, token))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.ConfirmEmail(userId, token);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);

            _mockEmailService.Verify(x => x.ConfirmEmail(userId, token), Times.Once);
        }

        #endregion

        #region ResendConfirmationEmail Tests

        [Fact]
        public async Task ResendConfirmationEmail_ValidUserId_ReturnsSuccess()
        {
            // Arrange
            var userId = "user123";
            var apiResponse = ApiResponse<object>.Success(
                "Confirmation email sent",
                (int)HttpStatusCode.OK
            );

            _mockEmailService
                .Setup(x => x.SendEmailConfirmationAsync(userId, It.IsAny<string>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.ResendConfirmationEmail(userId);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.True(response.IsSuccessfull);

            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync(userId, It.IsAny<string>()),
                Times.Once
            );
        }

        [Fact]
        public async Task ResendConfirmationEmail_EmptyUserId_ReturnsBadRequest()
        {
            // Arrange
            var userId = "";

            // Act
            var result = await _controller.ResendConfirmationEmail(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("User ID is required.", badRequestResult.Value);

            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never
            );
        }

        [Fact]
        public async Task ResendConfirmationEmail_NullUserId_ReturnsBadRequest()
        {
            // Arrange
            string userId = null;

            // Act
            var result = await _controller.ResendConfirmationEmail(userId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("User ID is required.", badRequestResult.Value);

            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never
            );
        }

        [Fact]
        public async Task ResendConfirmationEmail_EmailServiceFails_ReturnsError()
        {
            // Arrange
            var userId = "user123";
            var apiResponse = ApiResponse<object>.Failed(
                message: "Failed to send email",
                statusCode: (int)HttpStatusCode.InternalServerError
            );

            _mockEmailService
                .Setup(x => x.SendEmailConfirmationAsync(userId, It.IsAny<string>()))
                .ReturnsAsync(apiResponse);

            // Act
            var result = await _controller.ResendConfirmationEmail(userId);

            // Assert
            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
            var response = Assert.IsType<ApiResponse<object>>(objectResult.Value);
            Assert.False(response.IsSuccessfull);

            _mockEmailService.Verify(
                x => x.SendEmailConfirmationAsync(userId, It.IsAny<string>()),
                Times.Once
            );
        }

        #endregion
    }
}