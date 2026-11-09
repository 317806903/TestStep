namespace Snake.Logic.Protocol;

public enum MsgType : byte
{
    // 服务器 -> 客户端
    Welcome = 1, // playerId, playerCount
    Start = 2,   // GameConfig
    Frame = 3,   // frame, inputs[]
    Desync = 4,  // frame
    // 客户端 -> 服务器
    Input = 10,  // dir
    Hash = 11,   // frame, hash
}

/// <summary>极简二进制协议，第一个字节是 MsgType。只依赖 BCL，不绑定具体网络库。</summary>
public static class Msg
{
    public const string ConnectKey = "snake-lockstep-v1";

    public static byte[] Welcome(int playerId, int playerCount) =>
        Build(MsgType.Welcome, w => { w.Write((byte)playerId); w.Write((byte)playerCount); });

    public static byte[] Start(GameConfig c) => Build(MsgType.Start, w => WriteConfig(w, c));

    public static byte[] Frame(int frame, Dir[] inputs) => Build(MsgType.Frame, w =>
    {
        w.Write(frame);
        w.Write((byte)inputs.Length);
        foreach (var d in inputs) w.Write((byte)d);
    });

    public static byte[] Desync(int frame) => Build(MsgType.Desync, w => w.Write(frame));
    public static byte[] Input(Dir dir) => Build(MsgType.Input, w => w.Write((byte)dir));
    public static byte[] Hash(int frame, ulong hash) => Build(MsgType.Hash, w => { w.Write(frame); w.Write(hash); });

    public static (MsgType type, BinaryReader reader) Open(byte[] data)
    {
        var r = new BinaryReader(new MemoryStream(data));
        return ((MsgType)r.ReadByte(), r);
    }

    public static (int frame, Dir[] inputs) ReadFrame(BinaryReader r)
    {
        int frame = r.ReadInt32();
        var inputs = new Dir[r.ReadByte()];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = (Dir)r.ReadByte();
        return (frame, inputs);
    }

    public static void WriteConfig(BinaryWriter w, GameConfig c)
    {
        w.Write((byte)c.PlayerCount);
        w.Write((byte)c.Width);
        w.Write((byte)c.Height);
        w.Write(c.Seed);
        w.Write((byte)c.TickRate);
    }

    public static GameConfig ReadConfig(BinaryReader r) =>
        new(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadUInt32(), r.ReadByte());

    static byte[] Build(MsgType type, Action<BinaryWriter> body)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)type);
            body(w);
        }
        return ms.ToArray();
    }
}
