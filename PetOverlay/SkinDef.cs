using System.Windows.Media.Imaging;

namespace PetOverlay;

// A full alternate sprite set that gets swapped in wholesale (as opposed to
// PetRegistry's social layer or an accessory composited on top) - the base look
// is just a SkinDef like any other, not a special-cased fork, so ApplySprite()
// doesn't need an if/else between "base" and "everything else".
public class SkinDef
{
    public required string Name { get; init; }
    public required string Dir { get; init; } // source folder, for files not pre-loaded as BitmapImages (e.g. the radial menu icon)
    public required Dictionary<string, BitmapImage> Sprites { get; init; } // south/east/west/north
    public Dictionary<string, BitmapImage>? MoodSprites { get; init; }
    public List<BitmapImage>? TalkFrames { get; init; }
    public Dictionary<string, List<BitmapImage>>? WalkFrames { get; init; } // per direction, each a full cycle
}
