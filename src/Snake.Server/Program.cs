// 帧同步服务器：只转发输入，不跑游戏逻辑。
// 每个 tick 把所有玩家这一帧的输入打包广播；不等慢玩家（没输入就当 None）。
using System.Diagnostics;
using LiteNetLib;
using Snake.Logic;
using Snake.Logic.Protocol;

Console.OutputEncoding = System.Text.Encoding.UTF8;
int port = ArgInt("port", 9050);
int playerCount = Math.Clamp(ArgInt("players", 2), 1, 8);
int tickRate = Math.Clamp(ArgInt("tick", 10), 1, 60);
const int Width = 40, Height = 20;
const int MaxQueuedInputs = 3;

var peers = new NetPeer?[playerCount];
var online = new bool[playerCount];
// 每人一个输入队列，每帧消费一个：快速连按"上、左"两次转向不会丢
var pendingInputs = Enumerable.Range(0, playerCount).Select(_ => new Queue<Dir>()).ToArray();
var hashReports = new Dictionary<int, ulong?[]>();
GameConfig? config = null;
Replay? replay = null;
int frame = 0;
bool stop = false;
bool desynced = false;
var clock = new Stopwatch();
long nextTickMs = 0;
int frameMs = 1000 / tickRate;

var listener = new EventBasedNetListener();
var net = new NetManager(listener) { AutoRecycle = true };

listener.ConnectionRequestEvent += request =>
{
    if (config != null || Array.IndexOf(peers, null) < 0) { request.Reject(); return; }
    request.AcceptIfKey(Msg.ConnectKey);
};

listener.PeerConnectedEvent += peer =>
{
    int pid = Array.IndexOf(peers, null);
    if (pid < 0 || config != null) { peer.Disconnect(); return; }
    peers[pid] = peer;
    online[pid] = true;
    peer.Tag = pid;
    int count = online.Count(o => o);
    Log($"玩家 P{pid} 已连接 {peer.EndPoint}  ({count}/{playerCount})");
    peer.Send(Msg.Welcome(pid, playerCount), DeliveryMethod.ReliableOrdered);
    if (count == playerCount) StartGame();
};

listener.PeerDisconnectedEvent += (peer, info) =>
{
    if (peer.Tag is not int pid) return;
    online[pid] = false;
    if (config == null) peers[pid] = null; // 开局前离开：让出位置
    Log($"玩家 P{pid} 断开: {info.Reason}");
    if (config != null && !online.Any(o => o))
    {
        Log("所有玩家都已离开，结束本局");
        stop = true;
    }
};

listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
{
    if (peer.Tag is not int pid || config == null) return;
    try
    {
        var (type, r) = Msg.Open(reader.GetRemainingBytes());
        switch (type)
        {
            case MsgType.Input:
                var dir = (Dir)r.ReadByte();
                if (dir.IsValidMove() && pendingInputs[pid].Count < MaxQueuedInputs)
                    pendingInputs[pid].Enqueue(dir);
                break;
            case MsgType.Hash:
                OnHash(pid, r.ReadInt32(), r.ReadUInt64());
                break;
        }
    }
    catch (EndOfStreamException)
    {
        Log($"P{pid} 发来格式错误的包，已忽略");
    }
};

Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };

net.Start(port);
Log($"服务器已启动，端口 {port}，等待 {playerCount} 名玩家 (逻辑帧率 {tickRate}Hz)。Ctrl+C 结束");

while (!stop)
{
    net.PollEvents();
    // 用累加的 nextTickMs 而不是"上次时间 + 间隔"，偶尔卡顿后会自动追上，长期不漂移
    while (config != null && !stop && clock.ElapsedMilliseconds >= nextTickMs)
    {
        var inputs = new Dir[playerCount];
        for (int i = 0; i < playerCount; i++)
            if (pendingInputs[i].Count > 0) inputs[i] = pendingInputs[i].Dequeue();

        replay!.Frames.Add(inputs);
        net.SendToAll(Msg.Frame(frame, inputs), DeliveryMethod.ReliableOrdered);
        frame++;
        nextTickMs += frameMs;
    }
    Thread.Sleep(1);
}

if (replay is { Frames.Count: > 0 })
{
    var path = Path.GetFullPath($"replay_{DateTime.Now:yyyyMMdd_HHmmss}.snkrep");
    replay.Save(path);
    Log($"录像已保存: {path}  ({replay.Frames.Count} 帧)");
}
net.Stop();

void StartGame()
{
    config = new GameConfig(playerCount, Width, Height, (uint)Random.Shared.Next(), tickRate);
    replay = new Replay(config);
    net.SendToAll(Msg.Start(config), DeliveryMethod.ReliableOrdered);
    clock.Restart();
    nextTickMs = 0;
    Log($"人齐了，开局！seed={config.Seed}");
}

void OnHash(int pid, int f, ulong hash)
{
    if (!hashReports.TryGetValue(f, out var reports)) hashReports[f] = reports = new ulong?[playerCount];
    reports[pid] = hash;
    for (int i = 0; i < playerCount; i++)
        if (online[i] && reports[i] == null) return; // 还没收齐

    hashReports.Remove(f);
    if (!desynced && reports.Where(v => v != null).Distinct().Count() > 1)
    {
        desynced = true; // 一旦分叉就不会再合上，只报第一次（最有价值的定位点）
        Log($"!!! 第 {f} 帧不同步: " + string.Join("  ", reports.Select((v, i) => $"P{i}={v:X16}")));
        net.SendToAll(Msg.Desync(f), DeliveryMethod.ReliableOrdered);
    }
}

int ArgInt(string name, int fallback)
{
    int i = Array.IndexOf(args, "--" + name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
}

static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
