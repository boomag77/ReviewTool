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
            var json = File.ReadAllText(settingsPath);
            var payload = JsonSerializer.Deserialize<ReviewStatusSettingsPayload>(json, JsonOptions);
            if (payload is null)
            {
                error = "Settings file is empty or invalid.";
                return false;
            }

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

            var payload = new ReviewStatusSettingsPayload
            {
                Version = 1,
                RequiredStatuses = requiredStatuses.ToList(),
                CustomStatuses = customStatuses.ToList()
            };

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
    }
}
