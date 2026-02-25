using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ReviewTool;

internal static class AppSettings
{
    private const string AppFolderName = "ReviewTool";
    private const string StatusSettingsFileName = "review-status-flags.json";
    private const int DefaultFinalReviewThumbnailSizePx = 104;
    private const int DefaultFinalReviewThumbnailMaxSizePx = 160;
    private const int MinFinalReviewThumbnailSizePx = 48;
    private const int MaxFinalReviewThumbnailSizePx = 320;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryLoadReviewStatuses(out IReadOnlyList<ReviewStatus> requiredStatuses,
                                             out IReadOnlyList<ReviewStatus> customStatuses,
                                             out string error)
    {
        requiredStatuses = Array.Empty<ReviewStatus>();
        customStatuses = Array.Empty<ReviewStatus>();
        error = string.Empty;

        var settingsPath = BuildSettingsFilePath();
        if (!File.Exists(settingsPath))
        {
            return true;
        }

        try
        {
            var payload = TryReadPayload(settingsPath, out _, out _) ?? new ReviewStatusSettingsPayload();
            requiredStatuses = payload.RequiredStatuses is null
                ? Array.Empty<ReviewStatus>()
                : payload.RequiredStatuses;
            customStatuses = payload.CustomStatuses is null
                ? Array.Empty<ReviewStatus>()
                : payload.CustomStatuses;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load status settings: {ex.Message}";
            return false;
        }
    }

    public static bool TrySaveReviewStatuses(IReadOnlyList<ReviewStatus> requiredStatuses,
                                             IReadOnlyList<ReviewStatus> customStatuses,
                                             out string error)
    {
        error = string.Empty;

        try
        {
            var settingsPath = BuildSettingsFilePath();
            var settingsFolder = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrWhiteSpace(settingsFolder))
            {
                error = "Invalid settings folder path.";
                return false;
            }

            Directory.CreateDirectory(settingsFolder);

            var payload = TryReadPayload(settingsPath, out _, out _) ?? new ReviewStatusSettingsPayload();
            payload.Version = 2;
            payload.RequiredStatuses = requiredStatuses.ToList();
            payload.CustomStatuses = customStatuses.ToList();
            payload.FinalReviewThumbnailSizePx ??= DefaultFinalReviewThumbnailSizePx;
            payload.FinalReviewThumbnailMaxSizePx ??= DefaultFinalReviewThumbnailMaxSizePx;

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var tempPath = settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, settingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save status settings: {ex.Message}";
            return false;
        }
    }

    public static bool TryLoadFinalReviewThumbnailSettings(out int thumbnailSizePx,
                                                           out int thumbnailMaxSizePx,
                                                           out string error)
    {
        thumbnailSizePx = DefaultFinalReviewThumbnailSizePx;
        thumbnailMaxSizePx = DefaultFinalReviewThumbnailMaxSizePx;
        error = string.Empty;

        var settingsPath = BuildSettingsFilePath();
        if (!File.Exists(settingsPath))
        {
            return true;
        }

        try
        {
            var payload = TryReadPayload(settingsPath, out _, out _) ?? new ReviewStatusSettingsPayload();
            var loadedMax = payload.FinalReviewThumbnailMaxSizePx ?? DefaultFinalReviewThumbnailMaxSizePx;
            loadedMax = Math.Clamp(loadedMax, MinFinalReviewThumbnailSizePx, MaxFinalReviewThumbnailSizePx);
            var loadedSize = payload.FinalReviewThumbnailSizePx ?? DefaultFinalReviewThumbnailSizePx;
            loadedSize = Math.Clamp(loadedSize, MinFinalReviewThumbnailSizePx, loadedMax);

            thumbnailSizePx = loadedSize;
            thumbnailMaxSizePx = loadedMax;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load thumbnail settings: {ex.Message}";
            return false;
        }
    }

    public static bool TrySaveFinalReviewThumbnailSettings(int thumbnailSizePx,
                                                           int thumbnailMaxSizePx,
                                                           out string error)
    {
        error = string.Empty;

        try
        {
            var settingsPath = BuildSettingsFilePath();
            var settingsFolder = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrWhiteSpace(settingsFolder))
            {
                error = "Invalid settings folder path.";
                return false;
            }

            Directory.CreateDirectory(settingsFolder);
            var payload = TryReadPayload(settingsPath, out _, out _) ?? new ReviewStatusSettingsPayload();
            payload.Version = 2;
            var clampedMax = Math.Clamp(thumbnailMaxSizePx, MinFinalReviewThumbnailSizePx, MaxFinalReviewThumbnailSizePx);
            var clampedSize = Math.Clamp(thumbnailSizePx, MinFinalReviewThumbnailSizePx, clampedMax);
            payload.FinalReviewThumbnailMaxSizePx = clampedMax;
            payload.FinalReviewThumbnailSizePx = clampedSize;

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var tempPath = settingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, settingsPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to save thumbnail settings: {ex.Message}";
            return false;
        }
    }

    private static ReviewStatusSettingsPayload? TryReadPayload(string settingsPath,
                                                               out bool fileExists,
                                                               out string error)
    {
        fileExists = File.Exists(settingsPath);
        error = string.Empty;
        if (!fileExists)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<ReviewStatusSettingsPayload>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string BuildSettingsFilePath()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataFolder, AppFolderName, StatusSettingsFileName);
    }

    private sealed class ReviewStatusSettingsPayload
    {
        public int Version { get; set; }
        public List<ReviewStatus>? RequiredStatuses { get; set; }
        public List<ReviewStatus>? CustomStatuses { get; set; }
        public int? FinalReviewThumbnailSizePx { get; set; }
        public int? FinalReviewThumbnailMaxSizePx { get; set; }
    }
}
