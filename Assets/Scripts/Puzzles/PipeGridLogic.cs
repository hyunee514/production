using System;
using System.Collections.Generic;

namespace AutoSurvival.Puzzles
{
    // ───────────────────────────────────────────────────────────────
    // 순수 C# 로직 (UnityEngine 의존 없음 -> EditMode 단위 테스트 가능)
    // 배관/공압 라인 퍼즐: 회전 가능한 배관 조각으로 압력원 -> 실린더까지 '새지 않게' 연결
    // ───────────────────────────────────────────────────────────────

    [Flags]
    public enum Side { None = 0, N = 1, E = 2, S = 4, W = 8 }

    public enum PipeKind
    {
        Empty,
        Straight,   // pipe-large-long      : N+S
        Elbow,      // pipe-large-bend      : N+E
        Tee,        // pipe-large-junction  : N+E+W
        Cross,      // pipe-large-cross     : 전방향
        Source,     // 압력원 (compressor / machine-connection-pipe)
        Sink        // 액추에이터 (piston-round 등)
    }

    public class PipeCell
    {
        public PipeKind kind;
        public int rotation;        // 0~3 (시계방향 90도 단위)
        public bool locked;         // 고정 조각 (회전 불가)
        public bool wet;            // 계산 결과: 압력이 도달했는가

        public Side OpenSides => Rotate(BaseMask(kind), rotation);

        public static Side BaseMask(PipeKind k) => k switch
        {
            PipeKind.Straight => Side.N | Side.S,
            PipeKind.Elbow    => Side.N | Side.E,
            PipeKind.Tee      => Side.N | Side.E | Side.W,
            PipeKind.Cross    => Side.N | Side.E | Side.S | Side.W,
            PipeKind.Source   => Side.E,
            PipeKind.Sink     => Side.W,
            _ => Side.None
        };

        public static Side Rotate(Side mask, int quarterTurns)
        {
            int t = ((quarterTurns % 4) + 4) % 4;
            int m = (int)mask;
            for (int i = 0; i < t; i++)
                // 비트 순서 N(1)->E(2)->S(4)->W(8)->N 이므로 시계방향 = 1비트 좌측 순환
                m = ((m << 1) | (m >> 3)) & 0xF;
            return (Side)m;
        }
    }

    public class PipeFlowResult
    {
        public bool sinkReached;
        public int leakCount;                       // 열린 끝이 막혀있지 않은 곳 (공기/유체 누설)
        public int wetCount;
        public List<(int x, int y, Side side)> leaks = new();
    }

    public class PipeGridLogic
    {
        public readonly int Width, Height;
        private readonly PipeCell[,] _cells;

        public PipeGridLogic(int width, int height)
        {
            Width = width; Height = height;
            _cells = new PipeCell[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    _cells[x, y] = new PipeCell();
        }

        public PipeCell this[int x, int y] => _cells[x, y];
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public bool TryRotate(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var c = _cells[x, y];
            if (c.locked || c.kind == PipeKind.Empty) return false;
            c.rotation = (c.rotation + 1) & 3;
            return true;
        }

        // y는 위쪽이 작은 값(화면 좌표). N = y-1
        private static (int dx, int dy) Delta(Side s) => s switch
        {
            Side.N => (0, -1),
            Side.E => (1, 0),
            Side.S => (0, 1),
            _      => (-1, 0)
        };

        private static Side Opposite(Side s) => s switch
        {
            Side.N => Side.S, Side.S => Side.N, Side.E => Side.W, _ => Side.E
        };

        private static readonly Side[] AllSides = { Side.N, Side.E, Side.S, Side.W };

        /// <summary>Source에서 BFS로 압력 전파. 연결이 맞물리지 않는 열린 끝은 누설로 센다.</summary>
        public PipeFlowResult Simulate()
        {
            var result = new PipeFlowResult();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _cells[x, y].wet = false;

            var queue = new Queue<(int x, int y)>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (_cells[x, y].kind == PipeKind.Source)
                    {
                        _cells[x, y].wet = true;
                        queue.Enqueue((x, y));
                    }

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                var cell = _cells[cx, cy];
                result.wetCount++;
                if (cell.kind == PipeKind.Sink) result.sinkReached = true;

                foreach (var side in AllSides)
                {
                    if ((cell.OpenSides & side) == 0) continue;
                    var (dx, dy) = Delta(side);
                    int nx = cx + dx, ny = cy + dy;

                    bool connected = InBounds(nx, ny) &&
                                     (_cells[nx, ny].OpenSides & Opposite(side)) != 0;
                    if (!connected)
                    {
                        // Sink는 흐름의 종점이므로 열린 면이 비어도 누설로 세지 않음
                        if (cell.kind != PipeKind.Sink)
                        {
                            result.leakCount++;
                            result.leaks.Add((cx, cy, side));
                        }
                        continue;
                    }

                    var next = _cells[nx, ny];
                    if (!next.wet)
                    {
                        next.wet = true;
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            return result;
        }

        /// <summary>성공 조건: 액추에이터에 압력 도달 + 누설 0.</summary>
        public bool IsSolved(out PipeFlowResult result)
        {
            result = Simulate();
            return result.sinkReached && result.leakCount == 0;
        }
    }
}
