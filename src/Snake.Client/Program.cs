// 帧同步客户端：收到第 N 帧的全部输入才执行第 N 帧，本地不做预测。
using LiteNetLib;
using Snake.Client;
using Snake.Logic;
using Snake.Logic.Protocol;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (ArgStr("replay") is { } replayPath)
{
    ReplayPlayer.Run(replayPath);
    return;
}

string host = ArgStr("host") ?? "127.0.0.1";
int port = int.TryParse(ArgStr("port"), out var p) ? p : 9050;
// 调试用：从这一帧起（蛇活着时）故意篡改一次自己的输入，演示服务器的 hash 不同步检测
int desyncAt = int.TryParse(ArgStr("desync-at"), out var d) ? d : -1;
const int HashInterval = 10;

int myId = -1;
GameState? state = null;
ConsoleRenderer? renderer = null;
var frameQueue = new Queue<(int frame, Dir[] inputs)>();
string status = $"正在连接 {host}:{port} ...";
int desyncFrame = -1;
bool quit = false;

var listener = new EventBasedNetListener();
var net = new NetManager(listener) { AutoRecycle = true };

listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
{
    var (type, r) = Msg.Open(reader.GetRemainingBytes());
    switch (type)
    {
        case MsgType.Welcome:
            myId = r.ReadByte();
            int total = r.ReadByte();
            status = $"已连接，你是 P{myId}，等待其他玩家加入 (共需 {total} 人) ...";
            break;
        case MsgType.Start:
            var cfg = Msg.ReadConfig(r);
            state = new GameState(cfg);
            renderer = new ConsoleRenderer(cfg);
            Console.Clear();
            break;
        case MsgType.Frame:
            frameQueue.Enqueue(Msg.ReadFrame(r));
            break;
        case MsgType.Desync:
            desyncFrame = r.ReadInt32();
            break;
    }
};

listener.PeerDisconnectedEvent += (peer, info) =>
{
    status = $"与服务器断开: {info.Reason}";
    quit = true;
};

net.Start();
var server = net.Connect(host, port, Msg.ConnectKey);
Console.CursorVisible = false;
Console.Clear();
string? shownStatus = null;

try
{
    while (!quit)
    {
        net.PollEvents();

        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key is ConsoleKey.Q or ConsoleKey.Escape) { quit = true; status = "已退出"; break; }
            var dir = KeyMap.ToDir(key);
            if (dir != Dir.None && state != null) server.Send(Msg.Input(dir), DeliveryMethod.ReliableOrdered);
        }

        // 收到几帧就执行几帧（网络抖动后会一次性追上）
        bool dirty = false;
        while (state != null && frameQueue.Count > 0)
        {
            var (f, inputs) = frameQueue.Dequeue();
            if (f != state.Frame) throw new InvalidOperationException($"帧号错乱：本地 {state.Frame}，收到 {f}");
            if (desyncAt >= 0 && f >= desyncAt && state.Players[myId].Alive)
            {
                inputs = InjectDesync(state, inputs, myId); // 死蛇的输入会被忽略，所以等活着时再篡改
                desyncAt = -1;
            }

            state.Tick(inputs);
            if (state.Frame % HashInterval == 0)
                server.Send(Msg.Hash(state.Frame, state.ComputeHash()), DeliveryMethod.ReliableOrdered);
            dirty = true;
        }

        if (dirty)
        {
            renderer!.Draw(state!, myId, $"帧 {state!.Frame,6}   延迟 {server.Ping,4} ms   方向键/WASD 移动，Q 退出", desyncFrame);
        }
        else if (state == null && status != shownStatus)
        {
            Console.SetCursorPosition(0, 0);
            Console.WriteLine(status.PadRight(80));
            shownStatus = status;
        }

        Thread.Sleep(1);
    }
}
finally
{
    net.Stop();
    Console.ResetColor();
    Console.CursorVisible = true;
}
Console.WriteLine();
Console.WriteLine(status);

static Dir[] InjectDesync(GameState s, Dir[] inputs, int myId)
{
    var copy = (Dir[])inputs.Clone();
    copy[myId] = s.Players[myId].Dir is Dir.Up or Dir.Down ? Dir.Left : Dir.Up;
    return copy;
}

string? ArgStr(string name)
{
    int i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
