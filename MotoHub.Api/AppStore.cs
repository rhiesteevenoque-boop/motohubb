using System.Text.Json;

public sealed class AppStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppStore(IConfiguration configuration)
    {
        _filePath = configuration["MOTOHUB_DATA_PATH"] ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MotoHub", "motohub.json");
        if (File.Exists(_filePath))
        {
            Data = JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_filePath), JsonOptions)
                ?? throw new InvalidDataException($"MotoHub data file is empty or invalid: {_filePath}");
            return;
        }

        Data = StoreData.CreateSeed();
        SaveAsync().GetAwaiter().GetResult();
    }

    public StoreData Data { get; }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(Data, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

public sealed class StoreData
{
    public List<AccountRecord> Users { get; set; } = [];
    public List<ServiceRecord> Services { get; set; } = [];
    public List<PartRecord> Parts { get; set; } = [];
    public List<MotorcycleRecord> Motorcycles { get; set; } = [];
    public List<BookingRecord> Bookings { get; set; } = [];
    public List<CartItemRecord> CartItems { get; set; } = [];
    public List<NotificationRecord> Notifications { get; set; } = [];
    public Dictionary<string, Dictionary<string, JsonElement>> Preferences { get; set; } = [];
    public Dictionary<string, DateTimeOffset> RevokedTokens { get; set; } = [];

    public static StoreData CreateSeed()
    {
        var data = new StoreData
        {
            Services =
            [
                new() { Id = "oil", Name = "Oil Change", Description = "Premium oil and filter replacement", Price = 350, Icon = "◉" },
                new() { Id = "tune", Name = "Full Tune-up", Description = "Engine tune and complete inspection", Price = 650, Icon = "⚙" },
                new() { Id = "brake", Name = "Brake Service", Description = "Brake pads, fluid and safety check", Price = 500, Icon = "◈" },
                new() { Id = "check", Name = "General Check-up", Description = "A full condition and safety inspection", Price = 250, Icon = "✓" },
                new() { Id = "electrical", Name = "Electrical Diagnostics", Description = "Battery, lights and wiring checks", Price = 450, Icon = "ϟ" },
                new() { Id = "chain", Name = "Chain Cleaning", Description = "Clean, adjust and lubricate chain", Price = 200, Icon = "⛓" }
            ],
            Parts =
            [
                new() { Id = "oilpart", Name = "Engine Oil 1L", Category = "Maintenance", Price = 320, Icon = "◉" },
                new() { Id = "pads", Name = "Brake Pads", Category = "Brakes", Price = 480, Icon = "◈" },
                new() { Id = "chainpart", Name = "Drive Chain Kit", Category = "Drivetrain", Price = 1250, Icon = "⛓" },
                new() { Id = "plug", Name = "Spark Plug", Category = "Engine", Price = 180, Icon = "ϟ" },
                new() { Id = "filter", Name = "Air Filter", Category = "Engine", Price = 350, Icon = "▤" },
                new() { Id = "helmet", Name = "Full-face Helmet", Category = "Safety", Price = 2400, Icon = "⛑" },
                new() { Id = "gloves", Name = "Riding Gloves", Category = "Safety", Price = 650, Icon = "♧" },
                new() { Id = "tire", Name = "Street Tire", Category = "Tires", Price = 1850, Icon = "◉" }
            ]
        };
        data.Users.Add(new AccountRecord
        {
            Id = "admin-main",
            Name = "MotoHub Administrator",
            Email = "admin@motohub.com",
            Phone = "",
            Password = PasswordHasher.Hash("Admin123!"),
            Role = "admin"
        });
        return data;
    }
}

public sealed class AccountRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "user";
}

public sealed class ServiceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Icon { get; set; } = "";
}

public sealed class PartRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public string Icon { get; set; } = "";
}

public sealed class MotorcycleRecord
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }
    public string Plate { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Color { get; set; } = "";
}

public sealed class BookingRecord
{
    public string Id { get; set; } = "";
    public string Reference { get; set; } = "";
    public string UserId { get; set; } = "";
    public string BikeId { get; set; } = "";
    public string Bike { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string Service { get; set; } = "";
    public decimal Price { get; set; }
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CartItemRecord
{
    public string UserId { get; set; } = "";
    public string PartId { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class NotificationRecord
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset Date { get; set; }
}
