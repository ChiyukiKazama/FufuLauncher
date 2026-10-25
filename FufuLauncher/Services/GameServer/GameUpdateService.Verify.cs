/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    private async Task<List<AssetProperty>> VerifyAssetsAsync(GameUpdateContext context, SophonManifestProto manifest)
    {
        var brokenAssets = new ConcurrentBag<AssetProperty>();
        int totalAssets = manifest.Assets.Count;
        int doneAssets = 0;

        await Parallel.ForEachAsync(manifest.Assets, context.ParallelOptions, async (asset, token) =>
        {
            token.ThrowIfCancellationRequested();

            string assetPath = context.GetGameFilePath(asset.AssetName);
            bool isBroken;
            try
            {
                var info = new FileInfo(assetPath);
                isBroken = !info.Exists || info.Length != asset.AssetSize ||
                           !asset.AssetHashMd5.Equals(
                               await HashUtility.Md5FileAsync(assetPath, token).ConfigureAwait(false),
                               StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                isBroken = true;
            }

            if (isBroken)
            {
                brokenAssets.Add(asset);
            }

            int current = Interlocked.Increment(ref doneAssets);
            if (current % 100 == 0 || current == totalAssets)
            {
                context.Progress.Report(new GameServerConversionProgress(
                    string.Format("GameServer_VerifyProgress".GetLocalized(), current, totalAssets),
                    totalAssets, current, 0, 0, null));
            }
        }).ConfigureAwait(false);

        return brokenAssets.ToList();
    }
}