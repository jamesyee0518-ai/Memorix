using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Application.Validators;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class AuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAppDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ICurrentUserContext currentUser,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var validator = new RegisterRequestValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await _db.Users.AnyAsync(u => u.Email == email, ct);
        if (exists)
        {
            throw new DuplicateException("Email already registered");
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? email.Split('@')[0] : request.Nickname.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            PlanCode = "free",
            Status = "active",
            Timezone = "Asia/Shanghai",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        var token = _jwtTokenService.GenerateToken(user.Id, user.Email);
        var response = new RegisterResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Nickname = user.Nickname,
            Token = token
        };

        _logger.LogInformation("User registered: {UserId} ({Email})", user.Id, user.Email);
        return ApiResponse<RegisterResponse>.Ok(response);
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var validator = new LoginRequestValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user == null)
        {
            throw new AuthException("邮箱或密码错误");
        }

        if (user.Status != "active")
        {
            throw new AuthException("账号未启用");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new AuthException("邮箱或密码错误");
        }

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var token = _jwtTokenService.GenerateToken(user.Id, user.Email);
        var response = new LoginResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Nickname = user.Nickname,
            AvatarUrl = user.AvatarUrl,
            PlanCode = user.PlanCode,
            Token = token
        };

        _logger.LogInformation("User logged in: {UserId} ({Email})", user.Id, user.Email);
        return ApiResponse<LoginResponse>.Ok(response);
    }

    public async Task<ApiResponse<UserInfoResponse>> GetCurrentUserAsync(CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _currentUser.UserId.Value, ct);
        if (user == null)
        {
            throw new NotFoundException("User", _currentUser.UserId.Value);
        }

        return ApiResponse<UserInfoResponse>.Ok(Mapper.ToUserInfoResponse(user));
    }
}
