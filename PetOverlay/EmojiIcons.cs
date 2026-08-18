using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetOverlay;

// Classic WPF TextBlock has never rendered COLR/CPAL color emoji glyphs - they show
// up as flat monochrome outlines regardless of font choice. Rather than fight that,
// bubble text is split into plain-text runs plus small pre-rendered PNG icons
// (InlineUIContainer) wherever a known emoji character appears.
public static class EmojiIcons
{
    private static readonly Dictionary<string, string> RelativePaths = new()
    {
        ["😊"] = "icons/emoji/happy.png",
        ["😴"] = "icons/emoji/sleepy.png",
        ["🥱"] = "icons/emoji/bored.png",
        ["🤩"] = "icons/emoji/excited.png",
        ["👀"] = "icons/emoji/eyes.png",
        ["🍪"] = "icons/emoji/cookie.png",
        ["✨"] = "icons/emoji/sparkle.png",
        ["💕"] = "icons/emoji/heart.png",
        ["😈"] = "icons/emoji/devil.png",
        ["👋"] = "icons/menu/follow.png",
        ["😆"] = "icons/emoji/laugh.png",
        ["😳"] = "icons/emoji/flushed.png",
        ["🧶"] = "icons/emoji/yarn.png",
        ["🎉"] = "icons/emoji/party.png",
        ["👉"] = "icons/emoji/point.png",
        ["🎾"] = "toys/ball.png",
    };

    private static readonly Dictionary<string, BitmapImage> Cache = new();

    // Sets text on a TextBlock, replacing any recognized emoji with an inline icon
    // image and leaving everything else as plain text. Falls back to the raw emoji
    // character if its icon hasn't been generated/copied in yet.
    public static void SetRichText(TextBlock block, string text)
    {
        block.Inlines.Clear();
        var buffer = new StringBuilder();

        int i = 0;
        while (i < text.Length)
        {
            var matchedPath = MatchEmojiAt(text, i, out var matchedLength);
            if (matchedPath != null)
            {
                FlushBuffer(block, buffer);
                var image = TryLoadImage(matchedPath);
                if (image != null)
                {
                    block.Inlines.Add(new InlineUIContainer(BuildImageElement(image))
                    {
                        BaselineAlignment = BaselineAlignment.Center,
                    });
                }
                else
                {
                    buffer.Append(text, i, matchedLength);
                }
                i += matchedLength;
            }
            else
            {
                buffer.Append(text[i]);
                i++;
            }
        }

        FlushBuffer(block, buffer);
    }

    private static void FlushBuffer(TextBlock block, StringBuilder buffer)
    {
        if (buffer.Length == 0) return;
        block.Inlines.Add(new Run(buffer.ToString()));
        buffer.Clear();
    }

    private static string? MatchEmojiAt(string text, int index, out int matchedLength)
    {
        foreach (var (emoji, path) in RelativePaths)
        {
            if (index + emoji.Length <= text.Length &&
                string.CompareOrdinal(text, index, emoji, 0, emoji.Length) == 0)
            {
                matchedLength = emoji.Length;
                return path;
            }
        }
        matchedLength = 0;
        return null;
    }

    private static Image BuildImageElement(BitmapImage source)
    {
        var image = new Image
        {
            Source = source,
            Width = 15,
            Height = 15,
            Margin = new Thickness(1, 0, 1, -2),
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private static BitmapImage? TryLoadImage(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var cached)) return cached;

        var fullPath = Path.Combine(AppContext.BaseDirectory, "Assets", relativePath);
        if (!File.Exists(fullPath)) return null;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();

        Cache[relativePath] = bmp;
        return bmp;
    }
}
