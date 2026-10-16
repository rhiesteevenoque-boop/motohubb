using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class TokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public TokenService(IConfiguration configuration)
    {
        var configured = configuration["MOTOHUB_JWT_SECRET"];
        _key = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configured);
        if (_key.Length < 32)
            throw new InvalidOperationException("MOTOHUB_JWT_SECRET must contain at least 32 bytes.");
    }

    public string Create(AccountRecord account)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }, JsonOptions));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new TokenPayload(
            account.Id, account.Email, account.Role, now.ToUnixTimeSeconds(), now.AddHours(1).ToUnixTimeSeconds(),
            Guid.NewGuid().ToString("N")), JsonOptions));
        var message = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(message));
        return $"{message}.{Base64Url(signature)}";
    }

    public TokenPayload? Validate(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var message = $"{parts[0]}.{parts[1]}";
            var expected = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(message));
            if (!CryptographicOperations.FixedTimeEquals(expected, FromBase64Url(parts[2]))) return null;
            using var header = JsonDocument.Parse(FromBase64Url(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "HS256") return null;
            var payload = JsonSerializer.Deserialize<TokenPayload>(FromBase64Url(parts[1]), JsonOptions);
            return payload is not null && payload.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? payload : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }
}

public sealed record TokenPayload(string Subject, string Email, string Role, long IssuedAt, long ExpiresAt, string TokenId);

public sealed class BearerTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppStore _store;
    private readonly TokenService _tokens;

    public BearerTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppStore store,
        TokenService tokens) : base(options, logger, encoder)
    {
        _store = store;
        _tokens = tokens;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var token = _tokens.Validate(authorization["Bearer ".Length..].Trim());
        if (token is null || _store.Data.RevokedTokens.ContainsKey(token.TokenId))
            return Task.FromResult(AuthenticateResult.Fail("Bearer token is invalid, expired, or revoked."));
        var account = _store.Data.Users.FirstOrDefault(user => user.Id == token.Subject);
        if (account is null)
            return Task.FromResult(AuthenticateResult.Fail("Token account no longer exists."));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id),
            new Claim(ClaimTypes.Name, account.Name),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Role, account.Role),
            new Claim("jti", token.TokenId),
            new Claim("exp", token.ExpiresAt.ToString())
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
