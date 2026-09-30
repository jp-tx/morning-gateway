namespace MorningGateway.Models;

/// <summary>A calendar collection found while browsing a CalDAV server, offered to the user to add.</summary>
public record DiscoveredCalendar(string Url, string DisplayName, string? ColorHex);
