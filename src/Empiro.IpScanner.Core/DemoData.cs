namespace Empiro.IpScanner.Core;

/// <summary>Deterministic fictional inventory for previews; never derived from a network scan.</summary>
public static class DemoData
{
    public static List<DeviceResult> CreateDevices()
    {
        (int Ip, string Name, string Maker, string Ports, string Type, bool Favorite)[] rows =
        [
            (1, "Office Gateway", "Example Networks", "80, 443", "Router", true),
            (2, "Core Switch", "Example Networks", "22, 443", "Switch", true),
            (10, "Reception PC", "Example Systems", "445, 3389", "Workstation", false),
            (11, "Accounts PC", "Example Systems", "445, 3389", "Workstation", false),
            (12, "Design Laptop", "Example Systems", "445", "Laptop", false),
            (20, "Office Printer", "Example Print", "80, 443, 9100", "Printer", true),
            (30, "File Server", "Example Storage", "22, 443, 445", "Server", true),
            (40, "Lobby Access Point", "Example Wireless", "443", "Access Point", false),
            (50, "Front Door Camera", "Example Vision", "80, 443, 554", "Camera", true),
            (51, "Reception Camera", "Example Vision", "80, 554", "Camera", false),
            (52, "Parking Camera", "Example Vision", "443, 554", "Camera", false),
            (60, "Security Recorder", "Example Vision", "80, 443, 554", "Recorder", true)
        ];
        return rows.Select((row, index) => new DeviceResult
        {
            IpAddress = $"192.0.2.{row.Ip}", Name = row.Name, Manufacturer = row.Maker,
            MacAddress = $"02:00:00:00:00:{row.Ip:X2}", Ports = row.Ports, Type = row.Type,
            Status = "Online", ResponseTime = $"{index % 4 + 1} ms", IsFavorite = row.Favorite,
            Comment = "Fictional demo device; not a live discovery result.",
            LastSeen = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero)
        }).ToList();
    }

    public static List<ScanHistoryItem> CreateHistory() => Enumerable.Range(0, 5).Select(index => new ScanHistoryItem
    {
        StartedAt = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero).AddDays(-index),
        IpRange = "192.0.2.0/24", ScannedAddresses = 254, DevicesFound = 12 - index % 3,
        OnlineDevices = 12 - index % 3, DurationSeconds = 6 + index
    }).ToList();
}
