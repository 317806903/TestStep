using Snake.Logic.Protocol;

namespace Snake.Logic;

/// <summary>帧同步的录像 = 初始配置 + 每帧输入。几 KB 就能完整重现一整局。</summary>
public sealed class Replay
{
    const uint Magic = 0x524B4E53; // "SNKR"

    public Replay(GameConfig config) => Config = config;

    public GameConfig Config { get; }
    public List<Dir[]> Frames { get; } = new();

    public void Save(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Magic);
        Msg.WriteConfig(w, Config);
        w.Write(Frames.Count);
        foreach (var frame in Frames)
            foreach (var d in frame) w.Write((byte)d);
    }

    public static Replay Load(string path)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("不是贪吃蛇录像文件");
        var replay = new Replay(Msg.ReadConfig(r));
        int count = r.ReadInt32();
        for (int f = 0; f < count; f++)
        {
            var inputs = new Dir[replay.Config.PlayerCount];
            for (int i = 0; i < inputs.Length; i++) inputs[i] = (Dir)r.ReadByte();
            replay.Frames.Add(inputs);
        }
        return replay;
    }

    public GameState Simulate()
    {
        var state = new GameState(Config);
        foreach (var inputs in Frames) state.Tick(inputs);
        return state;
    }
}
