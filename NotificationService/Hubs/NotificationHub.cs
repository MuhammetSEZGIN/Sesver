using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NotificationService.Hubs;

[Authorize(AuthenticationSchemes = "Bearer")]
public class NotificationHub : Hub;
