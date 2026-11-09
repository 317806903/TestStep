namespace Snake.Logic;

public enum Dir : byte { None = 0, Up = 1, Down = 2, Left = 3, Right = 4 }

public static class DirExtensions
{
    public static bool IsValidMove(this Dir d) => d is >= Dir.Up and <= Dir.Right;

    public static bool IsOpposite(this Dir a, Dir b) =>
        (a, b) is (Dir.Up, Dir.Down) or (Dir.Down, Dir.Up) or (Dir.Left, Dir.Right) or (Dir.Right, Dir.Left);

    public static (int dx, int dy) Delta(this Dir d) => d switch
    {
        Dir.Up => (0, -1),
        Dir.Down => (0, 1),
        Dir.Left => (-1, 0),
        Dir.Right => (1, 0),
        _ => (0, 0),
    };
}

public readonly record struct Point(int X, int Y);

/// <summary>一局游戏的全部初始参数。双方只要这个相同、后续输入相同，结果就相同。</summary>
public sealed record GameConfig(int PlayerCount, int Width, int Height, uint Seed, int TickRate);

/// <summary>
/// 确定性随机数（xorshift32）。不要用 System.Random：不同 .NET 版本的实现可能不同。
/// </summary>
public sealed class DeterministicRandom
{
    public uint State { get; private set; }

    public DeterministicRandom(uint seed) => State = seed == 0 ? 0x9E3779B9u : seed;

    public uint NextUInt()
    {
        uint x = State;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        State = x;
        return x;
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt() % (uint)maxExclusive);
    }
}

public sealed class Player
{
    public Player(int id) => Id = id;

    public int Id { get; }
    /// <summary>蛇身，[0] 是蛇头。死亡时为空。</summary>
    public List<Point> Body { get; } = new();
    public Dir Dir { get; internal set; }
    public int Score { get; internal set; }
    public int Deaths { get; internal set; }
    public int PendingGrowth { get; internal set; }
    public int RespawnTimer { get; internal set; }

    public bool Alive => Body.Count > 0;
    public Point Head => Body[0];
}
