using System.IO;
using OpenDreamShared.Dream;
using OpenDreamShared.Resources;
using Robust.Client.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OpenDreamClient.Resources.ResourceTypes;

public sealed class DMIResource : DreamResource {
    public Texture Texture;
    public Vector2i IconSize;
    public DMIParser.ParsedDMIDescription Description;

    public readonly record struct DecodedDMI(Image<Rgba32> Image, DMIParser.ParsedDMIDescription Description);

    private readonly Dictionary<string, State> _states;

    public DMIResource(int id, byte[] data) : this(id, data, Decode(data)) { }

    /// <param name="decoded">The result of <see cref="Decode"/> for this data. Gets disposed.</param>
    public DMIResource(int id, byte[] data, DecodedDMI decoded) : base(id, data) {
        _states = new Dictionary<string, State>();
        ProcessDMIData(decoded);
    }

    public override void UpdateData(byte[] data) {
        base.UpdateData(data);
        ProcessDMIData(Decode(data));
    }

    /// <summary>
    /// The part of loading a DMI that can be done on any thread
    /// </summary>
    public static DecodedDMI Decode(byte[] data) {
        using Stream dmiStream = new MemoryStream(data);
        DMIParser.ParsedDMIDescription description = DMIParser.ParseDMI(dmiStream);

        dmiStream.Seek(0, SeekOrigin.Begin);

        return new(Image.Load<Rgba32>(dmiStream), description);
    }

    private void ProcessDMIData(DecodedDMI decoded) {
        using var image = decoded.Image;
        var description = decoded.Description;

        Texture = IoCManager.Resolve<IClyde>().LoadTextureFromImage(image, name: $"DMI Resource #{Id}");
        IconSize = new Vector2i(description.Width, description.Height);
        Description = description;

        _states.Clear();
        foreach (DMIParser.ParsedDMIState parsedState in description.States.Values) {
            State state = new State(Texture, parsedState, description.Width, description.Height);

            _states.Add(parsedState.Name, state);
        }
    }

    public State? GetState(string? stateName) {
        if (stateName == null || !_states.ContainsKey(stateName))
            return _states.TryGetValue(string.Empty, out var state) ? state : null; // Default state, if one exists

        return _states[stateName];
    }

    public ICursor? GetStateAsImage(IClyde clyde, string? stateName) {
        using var dmiStream = new MemoryStream(Data);
        var description = DMIParser.ParseDMI(dmiStream);

        dmiStream.Seek(0, SeekOrigin.Begin);

        Image<Rgba32> image = Image.Load<Rgba32>(dmiStream);
        var state = description.GetStateOrDefault(stateName);
        if (!(state?.Directions.TryGetValue(AtomDirection.South, out var frames) ?? false))
            return null;

        var stateImage = image.Clone(clone => {
            var frame = frames[0];

            clone.Crop(new Rectangle(frame.X, frame.Y, frame.X + description.Width, frame.Y + description.Height));
        });

        var hotspot = state.Hotspot ?? (0, stateImage.Height - 1); // Default to the top-left
        var cursor = clyde.CreateCursor(stateImage, hotspot);
        return cursor;
    }

    public struct State {
        public Dictionary<AtomDirection, AtlasTexture[]> Frames;

        public State(Texture texture, DMIParser.ParsedDMIState parsedState, int width, int height) {
            Frames = new Dictionary<AtomDirection, AtlasTexture[]>();

            foreach (KeyValuePair<AtomDirection, DMIParser.ParsedDMIFrame[]> pair in parsedState.Directions) {
                AtomDirection dir = pair.Key;
                DMIParser.ParsedDMIFrame[] parsedFrames = pair.Value;
                AtlasTexture[] frames = new AtlasTexture[parsedFrames.Length];

                for (int i = 0; i < parsedFrames.Length; i++) {
                    DMIParser.ParsedDMIFrame parsedFrame = parsedFrames[i];

                    frames[i] = new AtlasTexture(texture, new UIBox2(parsedFrame.X, parsedFrame.Y, parsedFrame.X + width, parsedFrame.Y + height));
                }

                Frames.Add(dir, frames);
            }
        }

        public AtlasTexture[] GetFrames(AtomDirection direction) {
            // Find another direction to use if this one doesn't exist
            if (!Frames.ContainsKey(direction)) {
                // The diagonal directions attempt to use east/west
                if (direction is AtomDirection.Northeast or AtomDirection.Southeast)
                    direction = AtomDirection.East;
                else if (direction is AtomDirection.Northwest or AtomDirection.Southwest)
                    direction = AtomDirection.West;

                // Use the south direction if the above still isn't valid
                if (!Frames.ContainsKey(direction))
                    direction = AtomDirection.South;
            }

            return Frames[direction];
        }
    }
}
