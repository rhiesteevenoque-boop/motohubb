using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:3000");
builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>("Bearer", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "MotoHub API" }));

var api = app.MapGroup("/api/v1");

api.MapPost("/auth/register", async (RegisterRequest request, AppStore store, TokenService tokens) =>
{
    var name = request.Name?.Trim();
    var email = request.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(name) || !IsEmail(email) || request.Password is null || request.Password.Length < 8)
        return BadRequest("Provide a name, valid email, and password of at least 8 characters.");
    if (store.Data.Users.Any(user => user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
        return Error(409, "An account with this email already exists.", "EMAIL_IN_USE");

    var account = new AccountRecord
    {
        Id = NewId("usr"),
        Name = name,
        Email = email!,
        Phone = request.Phone?.Trim() ?? "",
        Password = PasswordHasher.Hash(request.Password),
        Role = "user"
    };
    store.Data.Users.Add(account);
    await store.SaveAsync();
    return Results.Json(new AuthResponse(tokens.Create(account), "Bearer", ToAccount(account)), statusCode: 201);
});

api.MapPost("/auth/login", (LoginRequest request, AppStore store, TokenService tokens) =>
{
    var account = store.Data.Users.FirstOrDefault(user =>
        user.Email.Equals(request.Email?.Trim(), StringComparison.OrdinalIgnoreCase));
    if (account is null || !PasswordHasher.Verify(request.Password ?? "", account.Password))
        return Error(401, "Email or password is incorrect.", "INVALID_CREDENTIALS");
    return Results.Ok(new AuthResponse(tokens.Create(account), "Bearer", ToAccount(account)));
});

api.MapPost("/auth/logout", async (HttpContext context, AppStore store) =>
{
    var tokenId = context.User.FindFirstValue("jti");
    var expires = context.User.FindFirstValue("exp");
    if (!string.IsNullOrWhiteSpace(tokenId) && long.TryParse(expires, out var expiry))
    {
        store.Data.RevokedTokens[tokenId] = DateTimeOffset.FromUnixTimeSeconds(expiry);
        await store.SaveAsync();
    }
    return Results.NoContent();
}).RequireAuthorization();

api.MapPost("/auth/forgot-password", (ForgotPasswordRequest request) =>
    !IsEmail(request.Email) ? BadRequest("Provide a valid email address.") : Results.Accepted());

api.MapGet("/services", (AppStore store) =>
    Results.Ok(store.Data.Services.Select(ToService)));

api.MapGet("/parts", ([FromQuery] string? search, [FromQuery] string? category, AppStore store) =>
{
    var parts = store.Data.Parts.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(search))
        parts = parts.Where(part => (part.Name + " " + part.Category).Contains(search, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(category))
        parts = parts.Where(part => part.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(parts.Select(ToPart));
});

api.MapGet("/parts/{partId}", (string partId, AppStore store) =>
{
    var part = store.Data.Parts.FirstOrDefault(item => item.Id == partId);
    return part is null ? NotFound("Part not found.") : Results.Ok(ToPart(part));
});

var riders = api.MapGroup("").RequireAuthorization();
riders.MapGet("/me", (ClaimsPrincipal principal, AppStore store) =>
    Results.Ok(ToAccount(CurrentAccount(principal, store)!)));
riders.MapPatch("/me", async (ProfileUpdate request, ClaimsPrincipal principal, AppStore store) =>
{
    var account = CurrentAccount(principal, store)!;
    if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        return BadRequest("Name cannot be empty.");
    if (request.Email is not null && !IsEmail(request.Email))
        return BadRequest("Provide a valid email address.");
    var email = request.Email?.Trim().ToLowerInvariant();
    if (email is not null && store.Data.Users.Any(user => user.Id != account.Id && user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
        return Error(409, "Email is already in use.", "EMAIL_IN_USE");
    if (request.Name is not null) account.Name = request.Name.Trim();
    if (email is not null) account.Email = email;
    if (request.Phone is not null) account.Phone = request.Phone.Trim();
    await store.SaveAsync();
    return Results.Ok(ToAccount(account));
});

riders.MapGet("/me/preferences", (ClaimsPrincipal principal, AppStore store) =>
    Results.Ok(store.Data.Preferences.GetValueOrDefault(AccountId(principal), new Dictionary<string, JsonElement>())));
riders.MapPatch("/me/preferences", async (JsonElement request, ClaimsPrincipal principal, AppStore store) =>
{
    if (request.ValueKind != JsonValueKind.Object)
        return BadRequest("Preferences must be a JSON object.");
    var id = AccountId(principal);
    if (!store.Data.Preferences.TryGetValue(id, out var preferences))
        store.Data.Preferences[id] = preferences = new Dictionary<string, JsonElement>();
    foreach (var property in request.EnumerateObject())
        preferences[property.Name] = property.Value.Clone();
    await store.SaveAsync();
    return Results.Ok(preferences);
});

riders.MapGet("/motorcycles", (ClaimsPrincipal principal, AppStore store) =>
    Results.Ok(store.Data.Motorcycles.Where(bike => bike.UserId == AccountId(principal)).Select(ToMotorcycle)));
riders.MapPost("/motorcycles", async (MotorcycleInput request, ClaimsPrincipal principal, AppStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Make) || string.IsNullOrWhiteSpace(request.Model) || request.Year is < 1885 or > 2100)
        return BadRequest("Make, model, and a valid year are required.");
    var bike = new MotorcycleRecord
    {
        Id = NewId("bike"),
        UserId = AccountId(principal),
        Make = request.Make.Trim(),
        Model = request.Model.Trim(),
        Year = request.Year,
        Plate = request.Plate?.Trim() ?? "",
        Engine = request.Engine?.Trim() ?? "",
        Color = request.Color?.Trim() ?? ""
    };
    store.Data.Motorcycles.Add(bike);
    await store.SaveAsync();
    return Results.Json(ToMotorcycle(bike), statusCode: 201);
});
riders.MapDelete("/motorcycles/{motorcycleId}", async (string motorcycleId, ClaimsPrincipal principal, AppStore store) =>
{
    var bike = store.Data.Motorcycles.FirstOrDefault(item => item.Id == motorcycleId && item.UserId == AccountId(principal));
    if (bike is null) return NotFound("Motorcycle not found.");
    store.Data.Motorcycles.Remove(bike);
    await store.SaveAsync();
    return Results.NoContent();
});

riders.MapGet("/bookings", (ClaimsPrincipal principal, AppStore store) =>
    Results.Ok(store.Data.Bookings.Where(booking => booking.UserId == AccountId(principal)).Select(ToBooking)));
riders.MapPost("/bookings", async (BookingRequest request, ClaimsPrincipal principal, AppStore store) =>
{
    var userId = AccountId(principal);
    var service = store.Data.Services.FirstOrDefault(item => item.Id == request.ServiceId);
    var bike = store.Data.Motorcycles.FirstOrDefault(item => item.Id == request.MotorcycleId && item.UserId == userId);
    if (service is null || bike is null) return NotFound("Service or motorcycle not found.");
    if (!DateOnly.TryParse(request.Date, out var date) || date < DateOnly.FromDateTime(DateTime.UtcNow) ||
        string.IsNullOrWhiteSpace(request.Time))
        return BadRequest("Provide a valid future date and time.");

    var booking = new BookingRecord
    {
        Id = NewId("booking"),
        Reference = $"MH-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()[^8..]}",
        UserId = userId,
        BikeId = bike.Id,
        Bike = $"{bike.Make} {bike.Model}",
        ServiceId = service.Id,
        Service = service.Name,
        Price = service.Price,
        Date = date.ToString("yyyy-MM-dd"),
        Time = request.Time.Trim(),
        Notes = request.Notes?.Trim() ?? "",
        Status = "Pending",
        CreatedAt = DateTimeOffset.UtcNow
    };
    store.Data.Bookings.Insert(0, booking);
    store.Data.Notifications.Insert(0, new NotificationRecord
    {
        Id = NewId("notice"),
        UserId = userId,
        Text = $"Booking {booking.Reference} request received.",
        Date = DateTimeOffset.UtcNow
    });
    await store.SaveAsync();
    return Results.Json(ToBooking(booking), statusCode: 201);
});
riders.MapGet("/bookings/{bookingId}", (string bookingId, ClaimsPrincipal principal, AppStore store) =>
{
    var booking = store.Data.Bookings.FirstOrDefault(item => item.Id == bookingId && item.UserId == AccountId(principal));
    return booking is null ? NotFound("Booking not found.") : Results.Ok(ToBooking(booking));
});
riders.MapPatch("/bookings/{bookingId}/status", async (string bookingId, CancelBookingRequest request, ClaimsPrincipal principal, AppStore store) =>
{
    var booking = store.Data.Bookings.FirstOrDefault(item => item.Id == bookingId && item.UserId == AccountId(principal));
    if (booking is null) return NotFound("Booking not found.");
    if (request.Status != "Cancelled" || booking.Status is not ("Pending" or "Confirmed"))
        return Error(409, "Booking cannot be cancelled in its current state.", "INVALID_BOOKING_STATE");
    booking.Status = "Cancelled";
    await store.SaveAsync();
    return Results.Ok(ToBooking(booking));
});

riders.MapGet("/cart", (ClaimsPrincipal principal, AppStore store) => Results.Ok(ToCart(AccountId(principal), store)));
riders.MapPost("/cart/items", async (CartItemRequest request, ClaimsPrincipal principal, AppStore store) =>
{
    if (request.Quantity < 1) return BadRequest("Quantity must be at least 1.");
    var userId = AccountId(principal);
    if (!store.Data.Parts.Any(part => part.Id == request.PartId)) return NotFound("Part not found.");
    var item = store.Data.CartItems.FirstOrDefault(row => row.UserId == userId && row.PartId == request.PartId);
    if (item is null)
        store.Data.CartItems.Add(new CartItemRecord { UserId = userId, PartId = request.PartId, Quantity = request.Quantity });
    else
        item.Quantity += request.Quantity;
    await store.SaveAsync();
    return Results.Ok(ToCart(userId, store));
});
riders.MapPatch("/cart/items/{partId}", async (string partId, QuantityRequest request, ClaimsPrincipal principal, AppStore store) =>
{
    var item = store.Data.CartItems.FirstOrDefault(row => row.UserId == AccountId(principal) && row.PartId == partId);
    if (item is null) return NotFound("Cart item not found.");
    if (request.Quantity < 1) return BadRequest("Quantity must be at least 1.");
    item.Quantity = request.Quantity;
    await store.SaveAsync();
    return Results.Ok(ToCart(AccountId(principal), store));
});
riders.MapDelete("/cart/items/{partId}", async (string partId, ClaimsPrincipal principal, AppStore store) =>
{
    var item = store.Data.CartItems.FirstOrDefault(row => row.UserId == AccountId(principal) && row.PartId == partId);
    if (item is null) return NotFound("Cart item not found.");
    store.Data.CartItems.Remove(item);
    await store.SaveAsync();
    return Results.Ok(ToCart(AccountId(principal), store));
});
riders.MapPost("/cart/checkout", async (ClaimsPrincipal principal, AppStore store) =>
{
    var userId = AccountId(principal);
    var items = store.Data.CartItems.Where(row => row.UserId == userId).ToList();
    if (items.Count == 0) return BadRequest("Your cart is empty.");
    var total = items.Sum(row => store.Data.Parts.First(part => part.Id == row.PartId).Price * row.Quantity);
    store.Data.CartItems.RemoveAll(row => row.UserId == userId);
    await store.SaveAsync();
    return Results.Json(new { id = NewId("order"), total, status = "Demo order placed" }, statusCode: 201);
});
riders.MapGet("/notifications", (ClaimsPrincipal principal, AppStore store) =>
    Results.Ok(store.Data.Notifications.Where(item => item.UserId == AccountId(principal))
        .Select(item => new NotificationResponse(item.Id, item.Text, item.Date))));

var admin = api.MapGroup("/admin").RequireAuthorization(policy => policy.RequireRole("admin"));
admin.MapGet("/bookings", (AppStore store) => Results.Ok(store.Data.Bookings.Select(ToBooking)));
admin.MapPatch("/bookings/{bookingId}/status", async (string bookingId, AdminBookingStatusRequest request, AppStore store) =>
{
    var booking = store.Data.Bookings.FirstOrDefault(item => item.Id == bookingId);
    if (booking is null) return NotFound("Booking not found.");
    if (request.Status is not ("Pending" or "Confirmed" or "Completed" or "Cancelled"))
        return BadRequest("Status must be Pending, Confirmed, Completed, or Cancelled.");
    booking.Status = request.Status;
    await store.SaveAsync();
    return Results.Ok(ToBooking(booking));
});
admin.MapGet("/riders", (AppStore store) => Results.Ok(store.Data.Users.Where(user => user.Role == "user").Select(ToAccount)));
admin.MapPatch("/services/{serviceId}", async (string serviceId, ServicePriceRequest request, AppStore store) =>
{
    var service = store.Data.Services.FirstOrDefault(item => item.Id == serviceId);
    if (service is null) return NotFound("Service not found.");
    if (request.Price < 0) return BadRequest("Price cannot be negative.");
    service.Price = request.Price;
    await store.SaveAsync();
    return Results.Ok(ToService(service));
});

app.Run();

static bool IsEmail(string? email) =>
    !string.IsNullOrWhiteSpace(email) && System.Net.Mail.MailAddress.TryCreate(email, out var parsed) &&
    parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase);

static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
static string AccountId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
static AccountRecord? CurrentAccount(ClaimsPrincipal principal, AppStore store) =>
    store.Data.Users.FirstOrDefault(user => user.Id == AccountId(principal));

static AccountResponse ToAccount(AccountRecord user) =>
    new(user.Id, user.Name, user.Email, user.Phone, user.Role);
static ServiceResponse ToService(ServiceRecord service) =>
    new(service.Id, service.Name, service.Description, service.Price, service.Icon);
static PartResponse ToPart(PartRecord part) =>
    new(part.Id, part.Name, part.Category, part.Price, part.Icon);
static MotorcycleResponse ToMotorcycle(MotorcycleRecord bike) =>
    new(bike.Id, bike.Make, bike.Model, bike.Year, bike.Plate, bike.Engine, bike.Color);
static BookingResponse ToBooking(BookingRecord booking) =>
    new(booking.Id, booking.Reference, booking.ServiceId, booking.Service, booking.BikeId, booking.Bike,
        booking.Price, booking.Date, booking.Time, booking.Notes, booking.Status, booking.CreatedAt);
static CartResponse ToCart(string userId, AppStore store)
{
    var items = store.Data.CartItems.Where(row => row.UserId == userId)
        .Select(row => new CartResponseItem(row.PartId, row.Quantity,
            store.Data.Parts.First(part => part.Id == row.PartId) is var part
                ? ToPart(part)
                : throw new InvalidOperationException("Cart item references a missing part.")))
        .ToList();
    return new CartResponse(items, items.Sum(item => item.Part.Price * item.Quantity));
}
static IResult BadRequest(string message) => Error(400, message, "BAD_REQUEST");
static IResult NotFound(string message) => Error(404, message, "NOT_FOUND");
static IResult Error(int status, string message, string code) =>
    Results.Json(new ErrorResponse(message, code), statusCode: status);

public sealed record RegisterRequest(string? Name, string? Email, string? Password, string? Phone);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record ProfileUpdate(string? Name, string? Email, string? Phone);
public sealed record MotorcycleInput(string? Make, string? Model, int Year, string? Plate, string? Engine, string? Color);
public sealed record BookingRequest(string? ServiceId, string? MotorcycleId, string? Date, string? Time, string? Notes);
public sealed record CancelBookingRequest(string? Status);
public sealed record AdminBookingStatusRequest(string? Status);
public sealed record CartItemRequest(string? PartId, int Quantity = 1);
public sealed record QuantityRequest(int Quantity);
public sealed record ServicePriceRequest(decimal Price);
public sealed record AuthResponse(string AccessToken, string TokenType, AccountResponse Account);
public sealed record AccountResponse(string Id, string Name, string Email, string Phone, string Role);
public sealed record ServiceResponse(string Id, string Name, string Description, decimal Price, string Icon);
public sealed record PartResponse(string Id, string Name, string Category, decimal Price, string Icon);
public sealed record MotorcycleResponse(string Id, string Make, string Model, int Year, string Plate, string Engine, string Color);
public sealed record BookingResponse(string Id, string Reference, string ServiceId, string Service, string MotorcycleId,
    string Motorcycle, decimal Price, string Date, string Time, string Notes, string Status, DateTimeOffset CreatedAt);
public sealed record CartResponseItem(string PartId, int Quantity, PartResponse Part);
public sealed record CartResponse(List<CartResponseItem> Items, decimal Subtotal);
public sealed record NotificationResponse(string Id, string Text, DateTimeOffset Date);
public sealed record ErrorResponse(string Message, string Code);
