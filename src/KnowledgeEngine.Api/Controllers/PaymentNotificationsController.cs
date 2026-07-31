using System.Text;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payments")]
public sealed class PaymentNotificationsController : ControllerBase
{
    private readonly IPaymentService _payments;
    private readonly ILogger<PaymentNotificationsController> _logger;

    public PaymentNotificationsController(
        IPaymentService payments,
        ILogger<PaymentNotificationsController> logger)
    {
        _payments = payments;
        _logger = logger;
    }

    [HttpPost("wechat/notify")]
    public async Task<IActionResult> WeChatNotify(CancellationToken ct)
    {
        try
        {
            var body = await ReadBodyAsync(ct);
            await _payments.ProcessNotificationAsync(
                PaymentChannels.WeChat,
                body,
                ReadHeaders(),
                ct);
            return Ok(new { code = "SUCCESS", message = "成功" });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "WeChat Pay notification processing failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                code = "FAIL",
                message = "处理失败"
            });
        }
    }

    [HttpPost("alipay/notify")]
    public async Task<IActionResult> AlipayNotify(CancellationToken ct)
    {
        try
        {
            var body = await ReadBodyAsync(ct);
            await _payments.ProcessNotificationAsync(
                PaymentChannels.Alipay,
                body,
                ReadHeaders(),
                ct);
            return Content("success", "text/plain", Encoding.UTF8);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Alipay notification processing failed");
            return Content("failure", "text/plain", Encoding.UTF8);
        }
    }

    private async Task<string> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private Dictionary<string, string> ReadHeaders() =>
        Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
