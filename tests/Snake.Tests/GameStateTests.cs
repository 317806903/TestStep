using Snake.Logic;
using Xunit;

namespace Snake.Tests;

public class GameStateTests
{
    static GameConfig Config(int players, uint seed = 12345) => new(players, 40, 20, seed, 10);

    static Dir[] RandomInputs(Random rnd, int players)
    {
        var inputs = new Dir[players];
        for (int i = 0; i < players; i++) inputs[i] = (Dir)rnd.Next(0, 5);
        return inputs;
    }

    [Fact]
    public void SameSeedAndInputs_ProduceIdenticalStateEveryFrame()
    {
        var a = new GameState(Config(4));
        var b = new GameState(Config(4));
        var rnd = new Random(1);
        for (int f = 0; f < 5000; f++)
        {
            var inputs = RandomInputs(rnd, 4);
            a.Tick(inputs);
            b.Tick(inputs);
            Assert.Equal(a.ComputeHash(), b.ComputeHash());
        }
    }

    [Fact]
    public void DifferentSeeds_Diverge()
    {
        Assert.NotEqual(new GameState(Config(2, 1)).ComputeHash(), new GameState(Config(2, 2)).ComputeHash());
    }

    [Fact]
    public void ReplayRoundTrip_ReproducesFinalState()
    {
        var live = new GameState(Config(3));
        var replay = new Replay(live.Config);
        var rnd = new Random(7);
        for (int f = 0; f < 1000; f++)
        {
            var inputs = RandomInputs(rnd, 3);
            live.Tick(inputs);
            replay.Frames.Add(inputs);
        }

        var path = Path.GetTempFileName();
        try
        {
            replay.Save(path);
            Assert.Equal(live.ComputeHash(), Replay.Load(path).Simulate().ComputeHash());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReversingDirection_IsIgnored()
    {
        var s = new GameState(Config(1));
        s.Tick(new[] { Dir.None });
        s.Tick(new[] { Dir.None }); // 长到 3 节
        var p = s.Players[0];
        var before = p.Dir;
        var opposite = before switch { Dir.Left => Dir.Right, Dir.Right => Dir.Left, Dir.Up => Dir.Down, _ => Dir.Up };

        s.Tick(new[] { opposite });

        Assert.Equal(before, p.Dir);
        Assert.True(p.Alive);
    }

    [Fact]
    public void HittingWall_KillsThenRespawns()
    {
        // 出生点离墙 ≥3 格且朝向远端，直走必在第 21~37 帧撞墙，之后 29 帧复活
        var s = new GameState(Config(1));
        var none = new[] { Dir.None };
        for (int i = 0; i < 40; i++) s.Tick(none);
        Assert.Equal(1, s.Players[0].Deaths);
        Assert.False(s.Players[0].Alive);

        for (int i = 0; i < 30; i++) s.Tick(none);
        Assert.True(s.Players[0].Alive);
    }
}
