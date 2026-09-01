namespace Tracker.Core;

/// <summary>Novější vydání nalezené na GitHubu.</summary>
public sealed record AvailableUpdate(string Version, Uri DownloadUrl, Uri ReleaseUrl, string? Sha256);
