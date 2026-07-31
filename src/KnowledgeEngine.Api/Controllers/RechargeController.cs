using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/billing/recharge")]
public sealed class RechargeController : BaseController
{
    private readonly IPaymentService _payments;
    private readonly ICurrentUserContext _currentUser;

    public RechargeController(
        IPaymentService payments,
        ICurrentUserContext currentUser)
    {
        _payments = payments;
        _currentUser = currentUser;
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var response = await _payments.GetCatalogAsync(ct);
        return Ok(ApiResponse<RechargeCatalogResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateRechargeOrderRequest request,
        CancellationToken ct)
    {
        var response = await _payments.CreateOrderAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<RechargeOrderResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders(
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _payments.ListOrdersAsync(RequireUserId(), workspaceId, ct);
        return Ok(ApiResponse<RechargeOrderListResponse>.Ok(response, GetTraceId()));
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<IActionResult> GetOrder(
        Guid orderId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _payments.GetOrderAsync(RequireUserId(), workspaceId, orderId, ct);
        return Ok(ApiResponse<RechargeOrderResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("orders/{orderId:guid}/refresh")]
    public async Task<IActionResult> RefreshOrder(
        Guid orderId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _payments.RefreshOrderAsync(RequireUserId(), workspaceId, orderId, ct);
        return Ok(ApiResponse<RechargeOrderResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("orders/{orderId:guid}/close")]
    public async Task<IActionResult> CloseOrder(
        Guid orderId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _payments.CloseOrderAsync(RequireUserId(), workspaceId, orderId, ct);
        return Ok(ApiResponse<RechargeOrderResponse>.Ok(response, GetTraceId()));
    }

    [HttpPost("orders/{orderId:guid}/fake-confirm")]
    public async Task<IActionResult> ConfirmFakePayment(
        Guid orderId,
        [FromQuery] Guid workspaceId,
        CancellationToken ct)
    {
        var response = await _payments.ConfirmFakePaymentAsync(
            RequireUserId(),
            workspaceId,
            orderId,
            ct);
        return Ok(ApiResponse<RechargeOrderResponse>.Ok(response, GetTraceId()));
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
}
