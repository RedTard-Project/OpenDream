using Robust.Shared.Serialization;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Robust.Shared.Log;
using Robust.Shared.Maths;

namespace OpenDreamShared.Dream;

public enum HorizontalAnchor {
    West,
    Left,
    Center,
    East,
    Right
}

public enum VerticalAnchor {
    South,
    Bottom,
    Center,
    North,
    Top
}

[Serializable, NetSerializable]
public sealed class ScreenLocation {
    public string? MapControl;
    public HorizontalAnchor HorizontalAnchor;
    public VerticalAnchor VerticalAnchor;

    /// <summary>
    /// Whether the anchor had a SCREEN_ prefix, making it relative to the view and its borders instead of just the view
    /// </summary>
    public bool HorizontalScreenAnchor, VerticalScreenAnchor;

    public int X, Y;
    public int PixelOffsetX, PixelOffsetY;
    public ScreenLocation? Range;

    private static ISawmill Sawmill => Logger.GetSawmill("opendream.screen_loc_parser");

    private static string[] _keywords = [
        "CENTER",
        "WEST", "EAST", "LEFT", "RIGHT",
        "NORTH", "SOUTH", "TOP", "BOTTOM",
        "TOPLEFT", "TOPRIGHT",
        "BOTTOMLEFT", "BOTTOMRIGHT"
    ];

    public ScreenLocation(int x, int y, int pixelOffsetX, int pixelOffsetY, ScreenLocation? range = null) {
        X = x - 1;
        Y = y - 1;
        PixelOffsetX = pixelOffsetX;
        PixelOffsetY = pixelOffsetY;
        Range = range;
    }

    public ScreenLocation(int x, int y, int iconSize) {
        // These are negative when on the border outside the view
        X = (int)MathF.Floor((float)x / iconSize);
        Y = (int)MathF.Floor((float)y / iconSize);

        PixelOffsetX = x - X * iconSize;
        PixelOffsetY = y - Y * iconSize;
        Range = null;
    }

    public ScreenLocation(string screenLocation) {
        screenLocation = screenLocation.ToUpper(CultureInfo.InvariantCulture);

        ParseScreenLoc(screenLocation);
    }

    /// <param name="viewOffset">The position of the bottom-left of the view, not including its borders</param>
    public Vector2 GetViewPosition(Vector2 viewOffset, ViewRange view, ViewBorders borders, float tileSize, Vector2i iconSize) {
        // TODO: LEFT/RIGHT/TOP/BOTTOM need to stick to the edge of the visible map if the map's container is smaller than the map itself

        float x = (X + PixelOffsetX / tileSize);
        x += HorizontalAnchor switch {
            HorizontalAnchor.West or HorizontalAnchor.Left => HorizontalScreenAnchor ? -borders.Left : 0,
            HorizontalAnchor.Center => view.CenterX,
            HorizontalAnchor.East => view.Width - 1 + (HorizontalScreenAnchor ? borders.Right : 0),
            HorizontalAnchor.Right => view.Width - (iconSize.X / tileSize),
            _ => throw new Exception($"Invalid horizontal anchor {HorizontalAnchor}")
        };

        float y = (Y + PixelOffsetY / tileSize);
        y += VerticalAnchor switch {
            VerticalAnchor.South or VerticalAnchor.Bottom => VerticalScreenAnchor ? -borders.Bottom : 0,
            VerticalAnchor.Center => view.CenterY,
            VerticalAnchor.North => view.Height - 1 + (VerticalScreenAnchor ? borders.Top : 0),
            VerticalAnchor.Top => view.Height - (iconSize.Y / tileSize),
            _ => throw new Exception($"Invalid vertical anchor {VerticalAnchor}")
        };

        return viewOffset + new Vector2(x, y);
    }

    /// <summary>
    /// How many times a screen object using this is repeated on each axis
    /// </summary>
    /// <param name="step">The distance between each repeat, in tiles</param>
    public Vector2i GetRepeats(ViewRange view, ViewBorders borders, Vector2 step) {
        if (Range == null)
            return Vector2i.One;

        // Pixel offsets don't affect how many tiles the range covers
        var start = new Vector2(X, Y);
        var end = new Vector2(Range.X, Range.Y);
        start += GetAnchorTile(view, borders);
        end += Range.GetAnchorTile(view, borders);

        return new Vector2i(
            Math.Max(1, (int)MathF.Floor((end.X - start.X) / step.X) + 1),
            Math.Max(1, (int)MathF.Floor((end.Y - start.Y) / step.Y) + 1));
    }

    /// <summary>
    /// Expands the borders to fit a screen object using this
    /// </summary>
    /// <param name="iconTiles">The amount of tiles covered by the object's icon</param>
    public void ExpandBorders(ViewRange view, Vector2i iconTiles, ref ViewBorders borders) {
        // These are always within the map control
        bool horizontal = !HorizontalScreenAnchor && HorizontalAnchor is not HorizontalAnchor.Left and not HorizontalAnchor.Right;
        bool vertical = !VerticalScreenAnchor && VerticalAnchor is not VerticalAnchor.Bottom and not VerticalAnchor.Top;

        // Pixel offsets never create a border
        var tile = GetAnchorTile(view, default) + new Vector2i(X, Y);

        if (horizontal) {
            borders.Left = Math.Max(borders.Left, -tile.X);
            borders.Right = Math.Max(borders.Right, tile.X + iconTiles.X - view.Width);
        }

        if (vertical) {
            borders.Bottom = Math.Max(borders.Bottom, -tile.Y);
            borders.Top = Math.Max(borders.Top, tile.Y + iconTiles.Y - view.Height);
        }

        Range?.ExpandBorders(view, iconTiles, ref borders);
    }

    private Vector2i GetAnchorTile(ViewRange view, ViewBorders borders) {
        return new Vector2i(
            HorizontalAnchor switch {
                HorizontalAnchor.Center => view.CenterX,
                HorizontalAnchor.East or HorizontalAnchor.Right => view.Width - 1 + (HorizontalScreenAnchor ? borders.Right : 0),
                _ => HorizontalScreenAnchor ? -borders.Left : 0
            },
            VerticalAnchor switch {
                VerticalAnchor.Center => view.CenterY,
                VerticalAnchor.North or VerticalAnchor.Top => view.Height - 1 + (VerticalScreenAnchor ? borders.Top : 0),
                _ => VerticalScreenAnchor ? -borders.Bottom : 0
            });
    }

    public override string ToString() {
        string mapControl = MapControl != null ? $"{MapControl}:" : string.Empty;
        string range = Range != null ? $" to {Range}" : string.Empty;

        return $"{mapControl}{HorizontalAnchor}+{X+1}:{PixelOffsetX},{VerticalAnchor}+{Y+1}:{PixelOffsetY}{range}";
    }

    public string ToCoordinates() {
        return $"{X+1}:{PixelOffsetX},{Y+1}:{PixelOffsetY}";
    }

    private void ParseScreenLoc(string screenLoc) {
        string[] rangeSplit = screenLoc.Split(" TO ");
        Range = rangeSplit.Length > 1 ? new ScreenLocation(rangeSplit[1]) : null;

        string[] coordinateSplit = rangeSplit[0].Split(",");
        if (coordinateSplit.Length is not 1 and not 2)
            throw new Exception($"Invalid screen_loc \"{screenLoc}\"");

        int mapControlSplitIndex = coordinateSplit[0].IndexOf(':');
        if (mapControlSplitIndex > 0) {
            string mapControl = rangeSplit[0].Substring(0, mapControlSplitIndex);

            if (char.IsAsciiLetter(mapControl[0]) && mapControl.IndexOfAny(['+', '-']) == -1 && !_keywords.Contains(mapControl)) {
                MapControl = mapControl;
                coordinateSplit[0] = coordinateSplit[0].Substring(mapControlSplitIndex + 1);
            }
        }

        if (coordinateSplit.Length == 1) {
            X = 0;
            Y = 0;

            var keyword = StripScreenPrefix(coordinateSplit[0].Trim(), out bool screenAnchor);

            HorizontalScreenAnchor = VerticalScreenAnchor = screenAnchor;
            (HorizontalAnchor, VerticalAnchor) = keyword switch {
                "CENTER" => (HorizontalAnchor.Center, VerticalAnchor.Center),
                "NORTHWEST" => (HorizontalAnchor.West, VerticalAnchor.North),
                "TOPLEFT" => (HorizontalAnchor.Left, VerticalAnchor.Top),
                "NORTHEAST" => (HorizontalAnchor.East, VerticalAnchor.North),
                "TOPRIGHT" => (HorizontalAnchor.Right, VerticalAnchor.Top),
                "SOUTHWEST" => (HorizontalAnchor.West, VerticalAnchor.South),
                "BOTTOMLEFT" => (HorizontalAnchor.Left, VerticalAnchor.Bottom),
                "SOUTHEAST" => (HorizontalAnchor.East, VerticalAnchor.South),
                "BOTTOMRIGHT" => (HorizontalAnchor.Right, VerticalAnchor.Bottom),
                _ => throw new Exception($"Invalid screen_loc {screenLoc}")
            };

            return;
        }

        bool swappedAxis = ParseScreenLocCoordinate(coordinateSplit[0], true);
        ParseScreenLocCoordinate(coordinateSplit[1], swappedAxis);
    }

    /// <summary>
    /// SCREEN_SOUTHWEST, SCREEN_EAST, etc. are relative to the view and its borders instead of just the view
    /// </summary>
    private static string StripScreenPrefix(string keyword, out bool hadPrefix) {
        const string prefix = "SCREEN_";

        hadPrefix = keyword.StartsWith(prefix, StringComparison.Ordinal);
        return hadPrefix ? keyword[prefix.Length..] : keyword;
    }

    /// <summary>
    /// Parse a string value on either side of the comma in a screen_loc
    /// </summary>
    /// <returns>Whether this set a value on an axis that doesn't match the isHorizontal arg</returns>
    private bool ParseScreenLocCoordinate(string coordinate, bool isHorizontal) {
        List<string> pieces = new();
        StringBuilder currentPiece = new();

        foreach (var c in coordinate) {
            switch (c) {
                case ' ' or '\t':
                    continue;
                case '-' or '+' when (currentPiece.Length == 0 || currentPiece[^1] != ':'):
                    // Start a new piece
                    pieces.Add(currentPiece.ToString());
                    currentPiece.Clear();
                    break;
            }

            currentPiece.Append(c);
        }

        pieces.Add(currentPiece.ToString());

        bool settingHorizontal = isHorizontal;
        bool isFirstNumber = true;
        float coordinateResult = 0.0f;
        int pixelOffsetResult = 0;
        foreach (string piece in pieces) {
            if (string.IsNullOrEmpty(piece))
                continue;

            string offsetStr;

            int pixelOffsetSeparator = piece.IndexOf(':');
            if (pixelOffsetSeparator != -1) {
                offsetStr = piece.Substring(0, pixelOffsetSeparator);
                string pixelOffsetStr = piece.Substring(pixelOffsetSeparator + 1);

                if (!int.TryParse(pixelOffsetStr, out var pixelOffset))
                    Sawmill.Error($"Invalid pixel offset {pixelOffsetStr} in {coordinate}");

                pixelOffsetResult += pixelOffset;
            } else {
                offsetStr = piece;
            }

            switch (StripScreenPrefix(offsetStr, out bool screenAnchor)) {
                case "CENTER":
                    if (settingHorizontal)
                        HorizontalAnchor = HorizontalAnchor.Center;
                    else
                        VerticalAnchor = VerticalAnchor.Center;

                    break;
                case "WEST":
                    // Yes, this sets the horizontal anchor regardless of the isHorizontal arg.
                    // Every macro sets their respective axis regardless of which coordinate it's in
                    HorizontalAnchor = HorizontalAnchor.West;
                    settingHorizontal = true;
                    break;
                case "EAST":
                    HorizontalAnchor = HorizontalAnchor.East;
                    settingHorizontal = true;
                    break;
                case "NORTH":
                    VerticalAnchor = VerticalAnchor.North;
                    settingHorizontal = false;
                    break;
                case "SOUTH":
                    VerticalAnchor = VerticalAnchor.South;
                    settingHorizontal = false;
                    break;
                case "LEFT":
                    HorizontalAnchor = HorizontalAnchor.Left;
                    settingHorizontal = true;
                    break;
                case "RIGHT":
                    HorizontalAnchor = HorizontalAnchor.Right;
                    settingHorizontal = true;
                    break;
                case "TOP":
                    VerticalAnchor = VerticalAnchor.Top;
                    settingHorizontal = false;
                    break;
                case "BOTTOM":
                    VerticalAnchor = VerticalAnchor.Bottom;
                    settingHorizontal = false;
                    break;

                // A normal number
                default:
                    if (!float.TryParse(offsetStr, CultureInfo.InvariantCulture, out var offset))
                        Sawmill.Error($"Invalid offset {offsetStr} in {coordinate}");

                    coordinateResult += offset;
                    if (isFirstNumber) // Deal with us being 0-indexed while screen_loc is 1-indexed
                        coordinateResult -= 1.0f;

                    break;
            }

            if (screenAnchor) {
                if (settingHorizontal)
                    HorizontalScreenAnchor = true;
                else
                    VerticalScreenAnchor = true;
            }

            isFirstNumber = false;
        }

        // Convert decimal numbers to a pixel offset
        int wholeCoordinate = (int)Math.Floor(coordinateResult);
        double fractionalOffset = coordinateResult - wholeCoordinate;
        pixelOffsetResult += (int)(32 * fractionalOffset);

        if (settingHorizontal) {
            X = wholeCoordinate;
            PixelOffsetX = pixelOffsetResult;
        } else {
            Y = wholeCoordinate;
            PixelOffsetY = pixelOffsetResult;
        }

        return settingHorizontal != isHorizontal;
    }
}
