using System.Net.Http.Json;
using System.Text;
using Bridge.Data;
using Bridge.Data.Jobs;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Places pin preview signs and a lectern with full pin history inside a building.
/// Signs go on the south interior wall (ground floor). Lectern near the topic sign.
/// </summary>
public sealed class PinDisplayService(
    RconService rcon,
    HttpClient httpClient,
    ILogger<PinDisplayService> logger)
{
    private const int BaseY = -60;
    private const int HalfFootprint = 10;
    private const int MaxPreviewSigns = 6;
    private const int CharsPerLine = 15;
    private const int LinesPerSign = 4;
    private const int CharsPerSign = CharsPerLine * LinesPerSign; // 60

    /// <summary>
    /// Places preview signs and a lectern with written book for a pinned message.
    /// </summary>
    public async Task DisplayPinAsync(UpdateBuildingJobPayload payload, CancellationToken ct)
    {
        int bx = payload.CenterX + (payload.BuildingIndex / 2 - 3) * 24;
        int bz = payload.BuildingIndex % 2 == 0
            ? payload.CenterZ - 20
            : payload.CenterZ + 20;

        var style = (BuildingStyle)(Math.Abs(payload.ChannelId % 3));

        logger.LogInformation(
            "Placing pin display in building '{Name}' at ({BX},{BZ})",
            payload.ChannelName, bx, bz);

        await PlacePreviewSignsAsync(bx, bz, payload.Pin, ct);
        await PlaceLecternAsync(bx, bz, payload.Pin, payload.ChannelName, ct);
    }

    /// <summary>
    /// Places up to 6 wall signs on the south interior wall with a truncated preview.
    /// Signs are stacked from Y=BaseY+2 to BaseY+7 on the south wall interior.
    /// </summary>
    private async Task PlacePreviewSignsAsync(int bx, int bz, PinData pin, CancellationToken ct)
    {
        int signZ = bz + HalfFootprint - 1; // south interior wall
        int signX = bx - 5; // left side of south wall, away from existing floor signs at bx+3

        // Build preview text: "Author: content..."
        var preview = $"{pin.Author}: {pin.Content}";
        if (preview.Length > MaxPreviewSigns * CharsPerSign)
            preview = preview[..(MaxPreviewSigns * CharsPerSign)];

        var signCount = Math.Min(MaxPreviewSigns, (preview.Length + CharsPerSign - 1) / CharsPerSign);

        var commands = new List<string>();
        for (int i = 0; i < signCount; i++)
        {
            int startIdx = i * CharsPerSign;
            int remaining = preview.Length - startIdx;
            var chunk = preview.Substring(startIdx, Math.Min(CharsPerSign, remaining));

            int signY = BaseY + 2 + i; // stack vertically

            var lines = SplitIntoSignLines(chunk);
            var l1 = EscapeSignText(lines[0]);
            var l2 = EscapeSignText(lines[1]);
            var l3 = EscapeSignText(lines[2]);
            var l4 = EscapeSignText(lines[3]);

            commands.Add(
                $"setblock {signX} {signY} {signZ} minecraft:oak_wall_sign[facing=north]" +
                $"{{front_text:{{messages:['\"{l1}\"','\"{l2}\"','\"{l3}\"','\"{l4}\"']}}}}");
        }

        if (commands.Count > 0)
            await rcon.SendBatchAsync(commands, ct);

        logger.LogDebug("Placed {Count} preview signs at ({X},{Z})", signCount, signX, signZ);
    }

    /// <summary>
    /// Places a lectern with a written book containing the full pin content
    /// via the Paper plugin HTTP API.
    /// </summary>
    private async Task PlaceLecternAsync(int bx, int bz, PinData pin, string channelName, CancellationToken ct)
    {
        // Lectern position: ground floor, near the topic sign area
        int lecternX = bx - 5;
        int lecternY = BaseY + 1;
        int lecternZ = bz + HalfFootprint - 3;

        var title = $"#{channelName} Pins";
        var author = pin.Author;

        // Split content into book pages (~256 chars per page)
        var pages = SplitIntoBookPages(pin);

        var request = new
        {
            x = lecternX,
            y = lecternY,
            z = lecternZ,
            title = title.Length > 32 ? title[..32] : title,
            author = author.Length > 16 ? author[..16] : author,
            pages
        };

        try
        {
            var response = await httpClient.PostAsJsonAsync("/plugin/lectern", request, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Placed lectern with pin book at ({X},{Y},{Z})", lecternX, lecternY, lecternZ);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to place lectern via plugin API — falling back to RCON setblock");
            // Fallback: just place an empty lectern via RCON
            await rcon.SendSetBlockAsync(lecternX, lecternY, lecternZ, "minecraft:lectern[facing=north]", ct);
        }
    }

    private static List<string> SplitIntoBookPages(PinData pin)
    {
        const int MaxPageLength = 256;
        var pages = new List<string>();

        // Header page
        var header = $"Pinned by {pin.Author}\n{pin.Timestamp:yyyy-MM-dd HH:mm}\n\n---";
        pages.Add(header.Length > MaxPageLength ? header[..MaxPageLength] : header);

        // Content pages
        var content = pin.Content;
        for (int i = 0; i < content.Length; i += MaxPageLength)
        {
            var remaining = content.Length - i;
            pages.Add(content.Substring(i, Math.Min(MaxPageLength, remaining)));
        }

        return pages;
    }

    private static string[] SplitIntoSignLines(string text)
    {
        var lines = new string[LinesPerSign];
        for (int i = 0; i < LinesPerSign; i++)
        {
            int start = i * CharsPerLine;
            if (start < text.Length)
            {
                int len = Math.Min(CharsPerLine, text.Length - start);
                lines[i] = text.Substring(start, len);
            }
            else
            {
                lines[i] = "";
            }
        }
        return lines;
    }

    private static string EscapeSignText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("'", "\\'");
    }
}
