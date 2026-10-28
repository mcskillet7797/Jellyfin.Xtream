// Copyright (C) 2022  Kevin Jilissen

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client.Models;
using Jellyfin.Xtream.Service;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream;

/// <summary>
/// The live TV channels, browsable by the categories of the Xtream provider.
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class LiveChannel(ILogger<LiveChannel> logger) : IChannel, IDisableMediaSourceDisplay
{
    /// <inheritdoc />
    public string? Name => "Xtream Live TV";

    /// <inheritdoc />
    public string? Description => "Live IPTV from the Xtream-compatible server, grouped by category.";

    /// <inheritdoc />
    public string DataVersion => Plugin.Instance.DataVersion;

    /// <inheritdoc />
    public string HomePageUrl => string.Empty;

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures()
    {
        return new()
        {
            ContentTypes = [
                ChannelMediaContentType.Clip,
            ],
            MediaTypes = [
                ChannelMediaType.Video
            ],
        };
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
    {
        throw new ArgumentException("Unsupported image type: " + type);
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages()
    {
        return new List<ImageType>();
    }

    /// <inheritdoc />
    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(query.FolderId))
            {
                return await GetCategories(cancellationToken).ConfigureAwait(false);
            }

            Guid guid = Guid.Parse(query.FolderId);
            StreamService.FromGuid(guid, out int prefix, out int categoryId, out int _, out int _);
            if (prefix == StreamService.LiveCategoryPrefix)
            {
                return await GetStreams(categoryId, cancellationToken).ConfigureAwait(false);
            }

            return new ChannelItemResult()
            {
                TotalRecordCount = 0,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get channel items");
            throw;
        }
    }

    private static ChannelItemInfo CreateChannelItemInfo(StreamInfo stream)
    {
        ParsedName parsedName = StreamService.ParseName(stream.Name);
        MediaSourceInfo source = Plugin.Instance.StreamService.GetMediaSourceInfo(StreamType.Live, stream.StreamId);

        // Since 12.0 Jellyfin drops a channel source whose GUID id is not the id of its item;
        // a source without an id gets the id of the item.
        source.Id = null;

        return new ChannelItemInfo()
        {
            ContentType = ChannelMediaContentType.Clip,
            DateModified = StreamService.MediaSourceRevision,
            Id = StreamService.ToGuid(StreamService.LiveStreamPrefix, stream.StreamId, 0, 0).ToString(),
            ImageUrl = stream.StreamIcon,
            IsLiveStream = true,
            MediaSources = [source],
            MediaType = ChannelMediaType.Video,
            Name = parsedName.Title,
            Tags = new List<string>(parsedName.Tags),
            Type = ChannelItemType.Media,
        };
    }

    private async Task<ChannelItemResult> GetCategories(CancellationToken cancellationToken)
    {
        IEnumerable<Category> categories = await Plugin.Instance.StreamService.GetLiveCategories(cancellationToken).ConfigureAwait(false);
        List<ChannelItemInfo> items = new List<ChannelItemInfo>(
            categories.Select((Category category) => StreamService.CreateChannelItemInfo(StreamService.LiveCategoryPrefix, category)));
        return new()
        {
            Items = items,
            TotalRecordCount = items.Count
        };
    }

    private async Task<ChannelItemResult> GetStreams(int categoryId, CancellationToken cancellationToken)
    {
        IEnumerable<StreamInfo> streams = await Plugin.Instance.StreamService.GetLiveStreamsWithOverrides(cancellationToken).ConfigureAwait(false);
        List<ChannelItemInfo> items = [.. streams
            .Where((StreamInfo stream) => stream.CategoryId == categoryId)
            .OrderBy((StreamInfo stream) => stream.Num)
            .Select(CreateChannelItemInfo)];
        return new()
        {
            Items = items,
            TotalRecordCount = items.Count
        };
    }

    /// <inheritdoc />
    public bool IsEnabledFor(string userId)
    {
        return Plugin.Instance.Configuration.LiveTv.Count > 0;
    }
}
