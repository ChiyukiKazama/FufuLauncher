/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services
{
    public class DevBuildDetectionService : IDevBuildDetectionService
    {
        public bool IsDevBuild
        {
            get;
            private set;
        }

        public bool HasChecked
        {
            get;
            private set;
        }

        public Task<bool> DetectAsync(string serverVersion)
        {
            IsDevBuild = !AppVersionHelper.IsPreviewBuild &&
                         AppVersionHelper.IsNewerVersion(AppVersionHelper.NumericVersion, serverVersion);
            HasChecked = true;

            DebugLog.Info("DevBuildDetectionService", $"[DevBuildDetection] IsDevBuild={IsDevBuild}, " +
                                                      $"Local version={AppVersionHelper.NumericVersion}, server version={serverVersion}");
            return Task.FromResult(IsDevBuild);
        }
    }
}