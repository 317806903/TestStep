namespace Snake.Logic;

/// <summary>
/// 帧同步核心。规则：
///  - 只用整数，不用 float/double
///  - 不读系统时间，不用 System.Random，随机数只来自 <see cref="DeterministicRandom"/>
///  - 只遍历 List / 数组（顺序固定），不遍历 Dictionary/HashSet
/// 这样相同的 GameConfig 加上相同的输入序列，在任何机器上每一帧都得到相同的状态。
/// </summary>
public sealed class GameState
{
    public const int RespawnFrames = 30;
    public const int InitialLength = 3;
    const int SpawnMargin = 3;

    readonly List<Player> _players = new();
    readonly List<Point> _foods = new();
    readonly DeterministicRandom _rng;
    readonly int[] _occupancy; // 临时网格：每个格子被占用的次数

    public GameState(GameConfig config)
    {
        if (config.PlayerCount is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(config), "玩家数需在 1~8 之间");
        if (config.Width is < 10 or > 255 || config.Height is < 10 or > 255) throw new ArgumentOutOfRangeException(nameof(config), "地图尺寸需在 10~255 之间");

        Config = config;
        _rng = new DeterministicRandom(config.Seed);
        _occupancy = new int[config.Width * config.Height];

        for (int i = 0; i < config.PlayerCount; i++) _players.Add(new Player(i));
        foreach (var p in _players) Spawn(p);
        RefillFood();
    }

    public GameConfig Config { get; }
    /// <summary>已经执行过的帧数（下一次 Tick 将执行第 Frame 帧）。</summary>
    public int Frame { get; private set; }
    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<Point> Foods => _foods;
    int FoodTarget => Config.PlayerCount + 2;

    /// <summary>推进一帧。inputs[i] 是玩家 i 这一帧的输入。</summary>
    public void Tick(ReadOnlySpan<Dir> inputs)
    {
        if (inputs.Length != _players.Count)
            throw new ArgumentException($"需要 {_players.Count} 个输入，收到 {inputs.Length}", nameof(inputs));

        // 1) 应用输入（不允许直接掉头）
        foreach (var p in _players)
        {
            var d = inputs[p.Id];
            if (!p.Alive || !d.IsValidMove()) continue;
            if (p.Body.Count > 1 && d.IsOpposite(p.Dir)) continue;
            p.Dir = d;
        }

        // 2) 所有蛇同时移动
        foreach (var p in _players)
        {
            if (!p.Alive) continue;
            var (dx, dy) = p.Dir.Delta();
            p.Body.Insert(0, new Point(p.Head.X + dx, p.Head.Y + dy));
            if (p.PendingGrowth > 0) p.PendingGrowth--;
            else p.Body.RemoveAt(p.Body.Count - 1);
        }

        // 3) 统一判定碰撞：先全部判定、再统一处死，结果与遍历顺序无关
        RebuildOccupancy(includeFood: false);
        Span<bool> dies = stackalloc bool[_players.Count];
        foreach (var p in _players)
            dies[p.Id] = p.Alive && (!InBounds(p.Head) || _occupancy[Index(p.Head)] > 1);
        foreach (var p in _players)
            if (dies[p.Id]) Kill(p);

        // 4) 吃食物
        foreach (var p in _players)
        {
            if (!p.Alive) continue;
            int i = _foods.IndexOf(p.Head);
            if (i < 0) continue;
            _foods.RemoveAt(i);
            p.PendingGrowth++;
            p.Score++;
        }

        // 5) 复活倒计时
        foreach (var p in _players)
            if (!p.Alive && --p.RespawnTimer <= 0) Spawn(p);

        RefillFood();
        Frame++;
    }

    /// <summary>整个状态的指纹。客户端定期上报，服务器比对，不一致就说明出现了不同步。</summary>
    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL; // FNV-1a 64
        h = Mix(h, Frame);
        h = Mix(h, (int)_rng.State);
        foreach (var p in _players)
        {
            h = Mix(h, p.Id);
            h = Mix(h, (int)p.Dir);
            h = Mix(h, p.Score);
            h = Mix(h, p.Deaths);
            h = Mix(h, p.PendingGrowth);
            h = Mix(h, p.RespawnTimer);
            h = Mix(h, p.Body.Count);
            foreach (var c in p.Body) { h = Mix(h, c.X); h = Mix(h, c.Y); }
        }
        h = Mix(h, _foods.Count);
        foreach (var f in _foods) { h = Mix(h, f.X); h = Mix(h, f.Y); }
        return h;
    }

    static ulong Mix(ulong h, int v)
    {
        for (int i = 0; i < 4; i++)
        {
            h ^= (byte)(v >> (i * 8));
            h *= 1099511628211UL;
        }
        return h;
    }

    void Kill(Player p)
    {
        p.Body.Clear();
        p.PendingGrowth = 0;
        p.Deaths++;
        p.RespawnTimer = RespawnFrames;
    }

    void Spawn(Player p)
    {
        if (RandomFreeCell(SpawnMargin) is not { } c)
        {
            p.RespawnTimer = RespawnFrames; // 没地方了，过会儿再试
            return;
        }
        p.Body.Clear();
        p.Body.Add(c);
        p.Dir = c.X < Config.Width / 2 ? Dir.Right : Dir.Left; // 朝向地图较宽的一侧
        p.PendingGrowth = InitialLength - 1;
        p.RespawnTimer = 0;
    }

    void RefillFood()
    {
        while (_foods.Count < FoodTarget && RandomFreeCell(0) is { } c)
            _foods.Add(c);
    }

    /// <summary>先数出所有空格，再用确定性随机数挑第 k 个。保证一定能找到，且结果确定。</summary>
    Point? RandomFreeCell(int margin)
    {
        if (margin * 2 >= Math.Min(Config.Width, Config.Height)) margin = 0;
        RebuildOccupancy(includeFood: true);

        int free = 0;
        for (int y = margin; y < Config.Height - margin; y++)
            for (int x = margin; x < Config.Width - margin; x++)
                if (_occupancy[y * Config.Width + x] == 0) free++;
        if (free == 0) return null;

        int k = _rng.Next(free);
        for (int y = margin; y < Config.Height - margin; y++)
            for (int x = margin; x < Config.Width - margin; x++)
                if (_occupancy[y * Config.Width + x] == 0 && k-- == 0)
                    return new Point(x, y);

        throw new InvalidOperationException("unreachable");
    }

    void RebuildOccupancy(bool includeFood)
    {
        Array.Clear(_occupancy);
        foreach (var p in _players)
            foreach (var c in p.Body)
                if (InBounds(c)) _occupancy[Index(c)]++;
        if (includeFood)
            foreach (var f in _foods) _occupancy[Index(f)]++;
    }

    bool InBounds(Point c) => c.X >= 0 && c.Y >= 0 && c.X < Config.Width && c.Y < Config.Height;
    int Index(Point c) => c.Y * Config.Width + c.X;
}
