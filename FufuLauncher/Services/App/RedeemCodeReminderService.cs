/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;

namespace FufuLauncher.Helpers
{
    public class RedeemCodeReminderService
    {
        private readonly ILocalSettingsService _localSettingsService;

        private const string FingerprintSettingKey = "LastRedeemCodeFingerprint";
        private const string LastReminderDateSettingKey = "LastRedeemCodeReminderDate";

        public RedeemCodeReminderService(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;
        }


        public async Task CheckRedeemCodesAsync(Action<NotificationMessage> showNotificationAction)
        {
            try
            {
                bool isOs = false;
                var gamePathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
                if (gamePathObj is string gamePath && !string.IsNullOrEmpty(gamePath))
                {
                    var dir = gamePath;
                    if (System.IO.File.Exists(dir))
                        dir = System.IO.Path.GetDirectoryName(dir) ?? dir;
                    isOs = dir != null && System.IO.File.Exists(System.IO.Path.Combine(dir, "GenshinImpact.exe"));
                }


                var codesList = await FetchRedeemCodesAsync(isOs);
                if (codesList == null || codesList.Count == 0)
                    return;


                var currentFingerprint = ComputeFingerprint(codesList);
                var lastFingerprintObj = await _localSettingsService.ReadSettingAsync(FingerprintSettingKey);
                var lastFingerprint = lastFingerprintObj?.ToString();

                var todayStr = DateTime.Now.ToString("yyyy-MM-dd");


                if (string.IsNullOrEmpty(lastFingerprint))
                {
                    await _localSettingsService.SaveSettingAsync(FingerprintSettingKey, currentFingerprint);
                    DebugLog.Info("RedeemCodeReminderService",
                        "[RedeemCodes] Registered redeem code fingerprint for the first time, skipping notification");
                    return;
                }


                if (lastFingerprint != currentFingerprint)
                {
                    await NotifyNewCodesAsync(codesList, showNotificationAction);
                    await _localSettingsService.SaveSettingAsync(FingerprintSettingKey, currentFingerprint);
                    DebugLog.Info("RedeemCodeReminderService",
                        "[RedeemCodes] Redeem codes changed, notification pushed");
                    return;
                }


                await CheckExpiringTodayAsync(codesList, todayStr, showNotificationAction);
            }
            catch (Exception ex)
            {
                DebugLog.Info("RedeemCodeReminderService", $"[RedeemCodes] Redeem code check failed: {ex.Message}");
            }
        }


        private async Task<List<RedeemCodeItem>?> FetchRedeemCodesAsync(bool isOs)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
            };

            if (isOs)
            {
                var json = await client.GetStringAsync(ApiEndpoints.RedeemCodesOsUrl);
                var response = System.Text.Json.JsonSerializer.Deserialize<HoyoCodeResponse>(json, options);
                return response?.Codes?
                    .Where(c => string.Equals(c.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new RedeemCodeItem
                    {
                        Title = c.Rewards,
                        Codes = new List<string> { c.Code }
                    })
                    .ToList();
            }
            else
            {
                var json = await client.GetStringAsync(ApiEndpoints.RedeemCodesUrl);
                return System.Text.Json.JsonSerializer.Deserialize<List<RedeemCodeItem>>(json, options);
            }
        }


        private static string ComputeFingerprint(List<RedeemCodeItem> codes)
        {
            var sb = new StringBuilder();
            foreach (var code in codes.OrderBy(c => c.Title))
            {
                sb.Append(code.Title);
                sb.Append('|');
                foreach (var c in code.Codes.OrderBy(x => x))
                {
                    sb.Append(c);
                    sb.Append(',');
                }

                sb.Append('|');
                sb.Append(code.Valid);
                sb.Append('|');
                sb.Append(code.Time);
                sb.Append(';');
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }


        private static string FormatRemainingTime(string? validStr)
        {
            if (string.IsNullOrEmpty(validStr))
                return "Status_Unknown".GetLocalized();


            var match = System.Text.RegularExpressions.Regex.Match(validStr, @"(\d{4}[-/]\d{1,2}[-/]\d{1,2})");
            if (!match.Success)
                return validStr;

            if (DateTime.TryParse(match.Groups[1].Value, out var endDate))
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(validStr, @"(\d{4}[-/]\d{1,2}[-/]\d{1,2})");
                if (matches.Count >= 2)
                {
                    if (DateTime.TryParse(matches[1].Value, out var secondDate))
                        endDate = secondDate;
                }

                var remaining = endDate.Date - DateTime.Now.Date;
                if (remaining.TotalDays < 0)
                    return "RedeemCode_Expired".GetLocalized();
                else if (remaining.TotalDays == 0)
                    return "RedeemCode_ExpiringToday".GetLocalized();
                else if (remaining.TotalDays < 1)
                    return string.Format("RedeemCode_HoursLeft".GetLocalized(), remaining.Hours);
                else if (remaining.TotalDays < 30)
                    return string.Format("RedeemCode_DaysLeft".GetLocalized(), (int)remaining.TotalDays);
                else
                    return string.Format("RedeemCode_DaysLeft".GetLocalized(), (int)remaining.TotalDays);
            }

            return validStr;
        }


        private static async Task NotifyNewCodesAsync(List<RedeemCodeItem> codes,
            Action<NotificationMessage> showNotificationAction)
        {
            var recentCodes = codes.Take(5).ToList();

            var lines = new List<string>();
            var copyTextLines = new List<string>();
            foreach (var item in recentCodes)
            {
                var remaining = FormatRemainingTime(!string.IsNullOrEmpty(item.Valid) ? item.Valid : item.Time);
                var codesStr = string.Join(", ", item.Codes.Take(3));
                lines.Add($"• {item.Title} → {codesStr}（{remaining}）");
                copyTextLines.AddRange(item.Codes.Take(3));
            }

            if (codes.Count > 5)
            {
                lines.Add(string.Format("RedeemCode_MoreEvents".GetLocalized(), codes.Count - 5));
            }

            var msg = new NotificationMessage(
                "RedeemCode_NewPublished".GetLocalized(),
                string.Format("RedeemCode_NewContent".GetLocalized(), string.Join("\n", lines)),
                NotificationType.Information,
                0,
                string.Join("\n", copyTextLines)
            );

            showNotificationAction?.Invoke(msg);
            await Task.CompletedTask;
        }


        private async Task CheckExpiringTodayAsync(List<RedeemCodeItem> codes, string todayStr,
            Action<NotificationMessage> showNotificationAction)
        {
            var lastRemindedObj = await _localSettingsService.ReadSettingAsync(LastReminderDateSettingKey);
            if (lastRemindedObj != null && lastRemindedObj.ToString() == todayStr)
            {
                return;
            }

            var todaysCodes = codes.Where(c =>
                (!string.IsNullOrEmpty(c.Valid) && c.Valid.Contains(todayStr)) ||
                (!string.IsNullOrEmpty(c.Time) && c.Time.Contains(todayStr))
            ).ToList();

            if (todaysCodes.Count > 0)
            {
                var titles = string.Join("、", todaysCodes.Select(c => c.Title));
                var codesContent = string.Join("\n", todaysCodes.SelectMany(c => c.Codes));

                var msg = new NotificationMessage(
                    "RedeemCode_ExpiringTitle".GetLocalized(),
                    string.Format("RedeemCode_ExpiringContent".GetLocalized(), titles, codesContent),
                    NotificationType.Warning,
                    0,
                    codesContent
                );

                showNotificationAction?.Invoke(msg);
                await _localSettingsService.SaveSettingAsync(LastReminderDateSettingKey, todayStr);
            }
        }
    }
}