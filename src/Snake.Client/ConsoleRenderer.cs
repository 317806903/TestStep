using System.Text;
using Snake.Logic;

namespace Snake.Client;

/// <summary>表现层：只读 GameState，从不修改它。用背景色画格子，纯 ASCII，旧控制台也能显示。</summary>
sealed class ConsoleRenderer
{
    static readonly ConsoleColor[] HeadColors =
    {
        ConsoleColor.Green, ConsoleColor.Cyan, ConsoleColor.Yellow, ConsoleColor.Magenta,
        ConsoleColor.Blue, ConsoleColor.Red, ConsoleColor.White, ConsoleColor.Gray,
    };
    static readonly ConsoleColor[] BodyColors =
    {
        ConsoleColor.DarkGreen, ConsoleColor.DarkCyan, ConsoleColor.DarkYellow, ConsoleColor.DarkMagenta,
        ConsoleColor.DarkBlue, ConsoleColor.DarkRed, ConsoleColor.Gray, ConsoleColor.DarkGray,
    };

    readonly record struct Cell(ConsoleColor Bg, ConsoleColor Fg, string Text);
    static readonly Cell Empty = new(ConsoleColor.Black, ConsoleColor.Black, "  ");
    static readonly Cell Wall = new(ConsoleColor.DarkGray, ConsoleColor.DarkGray, "  ");
    static readonly Cell Food = new(ConsoleColor.Black, ConsoleColor.Red, "()");

    readonly GameConfig _cfg;
    readonly Cell[] _cells;
    readonly StringBuilder _run = new();
    ConsoleColor _runBg, _runFg;

    public ConsoleRenderer(GameConfig cfg)
    {
        _cfg = cfg;
        _cells = new Cell[cfg.Width * cfg.Height];
    }

    public void Draw(GameState s, int myId, string header, int desyncFrame)
    {
        int w = _cfg.Width, h = _cfg.Height;
        Array.Fill(_cells, Empty);
        foreach (var f in s.Foods) _cells[f.Y * w + f.X] = Food;
        foreach (var p in s.Players)
        {
            for (int i = p.Body.Count - 1; i >= 0; i--)
            {
                var c = p.Body[i];
                _cells[c.Y * w + c.X] = i == 0
                    ? new Cell(HeadColors[p.Id], ConsoleColor.Black, p.Id == myId ? "@@" : "  ")
                    : new Cell(BodyColors[p.Id], ConsoleColor.Black, "  ");
            }
        }

        Console.SetCursorPosition(0, 0);
        Console.ResetColor();
        Console.WriteLine(header.PadRight(80));

        for (int y = -1; y <= h; y++)
        {
            for (int x = -1; x <= w; x++)
            {
                bool border = x < 0 || y < 0 || x >= w || y >= h;
                Put(border ? Wall : _cells[y * w + x]);
            }
            Flush();
            Console.ResetColor();
            Console.WriteLine();
        }

        foreach (var p in s.Players)
        {
            Console.BackgroundColor = HeadColors[p.Id];
            Console.Write("  ");
            Console.ResetColor();
            string me = p.Id == myId ? "(你)" : "    ";
            string state = p.Alive ? "" : $"复活倒计时 {p.RespawnTimer,2}";
            Console.WriteLine($" P{p.Id}{me}  长度 {p.Body.Count,3}  得分 {p.Score,3}  死亡 {p.Deaths,3}  {state}".PadRight(70));
        }

        if (desyncFrame >= 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"!!! 服务器检测到不同步：第 {desyncFrame} 帧 hash 不一致".PadRight(70));
            Console.ResetColor();
        }
    }

    void Put(Cell c)
    {
        if (_run.Length > 0 && (c.Bg != _runBg || c.Fg != _runFg)) Flush();
        _runBg = c.Bg;
        _runFg = c.Fg;
        _run.Append(c.Text);
    }

    void Flush()
    {
        if (_run.Length == 0) return;
        Console.BackgroundColor = _runBg;
        Console.ForegroundColor = _runFg;
        Console.Write(_run.ToString());
        _run.Clear();
    }
}
