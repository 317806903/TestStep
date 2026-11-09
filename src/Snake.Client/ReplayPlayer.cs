using Snake.Logic;

namespace Snake.Client;

static class KeyMap
{
    public static Dir ToDir(ConsoleKey key) => key switch
    {
        ConsoleKey.UpArrow or ConsoleKey.W => Dir.Up,
        ConsoleKey.DownArrow or ConsoleKey.S => Dir.Down,
        ConsoleKey.LeftArrow or ConsoleKey.A => Dir.Left,
        ConsoleKey.RightArrow or ConsoleKey.D => Dir.Right,
        _ => Dir.None,
    };
}

/// <summary>回放：用同一个 GameState，喂录像里的输入，画面与当时完全一致。</summary>
static class ReplayPlayer
{
    public static void Run(string path)
    {
        var replay = Replay.Load(path);
        var state = new GameState(replay.Config);
        var renderer = new ConsoleRenderer(replay.Config);
        int frameMs = 1000 / replay.Config.TickRate;
        int total = replay.Frames.Count;
        bool paused = false;

        Console.CursorVisible = false;
        Console.Clear();
        try
        {
            int i = 0;
            while (i < total)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key is ConsoleKey.Q or ConsoleKey.Escape) return;
                    if (key == ConsoleKey.Spacebar) paused = !paused;
                }
                if (!paused)
                {
                    state.Tick(replay.Frames[i++]);
                    renderer.Draw(state, -1, $"回放 {i,6}/{total}   空格 暂停，Q 退出", -1);
                }
                Thread.Sleep(frameMs);
            }
        }
        finally
        {
            Console.ResetColor();
            Console.CursorVisible = true;
        }
        Console.WriteLine($"回放结束：共 {state.Frame} 帧，最终 hash = {state.ComputeHash():X16}");
    }
}
