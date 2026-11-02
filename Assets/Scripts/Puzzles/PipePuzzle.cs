using UnityEngine;
using AutoSurvival.Core;

namespace AutoSurvival.Puzzles
{
    /// <summary>
    /// 퍼즐 A: "새는 공압 라인 복구". 3D 월드에서 기계 패널에 카메라가 붙고 격자 배관을 회전시킨다.
    /// 프리팹 매핑(Kenney Factory Kit): pipe-large-long / -bend / -junction / -cross, machine-connection-pipe, piston-round.
    /// </summary>
    public class PipePuzzle : PuzzleSession
    {
        [Header("프리팹 (Factory Kit)")]
        [SerializeField] private GameObject straightPrefab;
        [SerializeField] private GameObject elbowPrefab;
        [SerializeField] private GameObject teePrefab;
        [SerializeField] private GameObject crossPrefab;
        [SerializeField] private GameObject sourcePrefab;
        [SerializeField] private GameObject sinkPrefab;
        [SerializeField] private GameObject leakVfxPrefab;   // 공기 새는 파티클
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private LayerMask pipeLayer;
        [SerializeField] private Camera puzzleCamera;

        private PipeGridLogic _grid;
        private GameObject[,] _views;
        private PipeFlowResult _last;
        private bool _solved;

        protected override void Setup(int machineTier)
        {
            concept = Scenario != null ? Scenario.concept : ConceptTag.PipeFitting;
            int size = Scenario != null && Scenario.pipeGridSize > 0
                ? Mathf.Clamp(Scenario.pipeGridSize, 4, 7)       // DB가 지정한 크기
                : Mathf.Clamp(3 + machineTier, 4, 7);            // 기본: 티어1 4x4 ... 티어4+ 7x7
            _grid = new PipeGridLogic(size, size);
            GenerateSolvableLayout(size);
            Scramble();
            BuildViews();
            Refresh();
        }

        /// <summary>
        /// 풀 수 있음이 보장된 레이아웃: Source(0,mid) -> Sink(size-1,mid)로 경로를 먼저 깔고,
        /// 이후 회전만 뒤섞는다. (랜덤 생성 후 풀 수 없는 퍼즐이 나오는 일 방지)
        /// </summary>
        private void GenerateSolvableLayout(int size)
        {
            int mid = size / 2;
            _grid[0, mid].kind = PipeKind.Source; _grid[0, mid].locked = true;
            _grid[size - 1, mid].kind = PipeKind.Sink; _grid[size - 1, mid].locked = true;

            // 직선 줄기 + 중간에 한 번 위/아래로 꺾는 우회로 하나
            int detourX = Random.Range(1, size - 2);
            int detourY = mid + (Random.value < 0.5f ? -1 : 1);
            for (int x = 1; x < size - 1; x++)
            {
                if (x == detourX)
                {
                    SetPipe(x, mid, PipeKind.Elbow, DirToElbow(detourY < mid ? Side.N : Side.S, Side.W));
                    SetPipe(x, detourY, PipeKind.Elbow, DirToElbow(detourY < mid ? Side.S : Side.N, Side.E));
                    SetPipe(x + 1, detourY, PipeKind.Elbow, DirToElbow(detourY < mid ? Side.N : Side.S, Side.W));
                    SetPipe(x + 1, mid, PipeKind.Elbow, DirToElbow(detourY < mid ? Side.S : Side.N, Side.E));
                    x++;
                }
                else if (_grid[x, mid].kind == PipeKind.Empty)
                    SetPipe(x, mid, PipeKind.Straight, 1);        // N+S를 90도 회전 = E+W
            }

            // 방해용 '가짜' 조각: 경로 밖 빈칸에 일부 채움 (누설 유발용 함정은 Tee/Cross)
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    if (_grid[x, y].kind == PipeKind.Empty && Random.value < 0.25f)
                        _grid[x, y].kind = Random.value < 0.5f ? PipeKind.Straight : PipeKind.Elbow;
        }

        // 두 열린 면(a, b)을 만들기 위한 Elbow 회전값. Elbow 기본 = N+E (rotation 0).
        private static int DirToElbow(Side a, Side b)
        {
            for (int r = 0; r < 4; r++)
            {
                var m = PipeCell.Rotate(Side.N | Side.E, r);
                if (m == (a | b)) return r;
            }
            return 0;
        }

        private void SetPipe(int x, int y, PipeKind k, int rot)
        {
            _grid[x, y].kind = k; _grid[x, y].rotation = rot; _grid[x, y].locked = false;
        }

        private void Scramble()
        {
            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (!_grid[x, y].locked)
                        _grid[x, y].rotation = Random.Range(0, 4);
        }

        private void BuildViews()
        {
            _views = new GameObject[_grid.Width, _grid.Height];
            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                {
                    var prefab = PrefabFor(_grid[x, y].kind);
                    if (prefab == null) continue;
                    var go = Instantiate(prefab, transform);
                    go.transform.localPosition = new Vector3(x * cellSize, 0f, -y * cellSize);
                    _views[x, y] = go;
                }
        }

        private GameObject PrefabFor(PipeKind k) => k switch
        {
            PipeKind.Straight => straightPrefab,
            PipeKind.Elbow => elbowPrefab,
            PipeKind.Tee => teePrefab,
            PipeKind.Cross => crossPrefab,
            PipeKind.Source => sourcePrefab,
            PipeKind.Sink => sinkPrefab,
            _ => null
        };

        protected override void Update()
        {
            if (IsRunning && Input.GetMouseButtonDown(0) && puzzleCamera != null)
                HandleClick();
            base.Update();
        }

        private void HandleClick()
        {
            var ray = puzzleCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit, 100f, pipeLayer)) return;

            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (_views[x, y] != null && hit.transform.IsChildOf(_views[x, y].transform))
                    {
                        if (_grid.TryRotate(x, y)) Refresh();
                        return;
                    }
        }

        private void Refresh()
        {
            _solved = _grid.IsSolved(out _last);

            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                {
                    if (_views[x, y] == null) continue;
                    _views[x, y].transform.localRotation = Quaternion.Euler(0f, 90f * _grid[x, y].rotation, 0f);
                    // TODO: wet 상태면 파이프 머티리얼 발광 (물/공기 흐름 연출)
                }

            // 누설 지점에 공기 새는 이펙트 + 쉬익 소리. 여기서 '왜 틀렸는지'가 시각·청각으로 즉시 전달된다.
            // TODO: 기존 VFX 풀링 후 _last.leaks 위치에 leakVfxPrefab 배치
        }

        protected override bool CheckSolved() => _solved;

        private void OnDestroy()
        {
            // 퍼즐이 끝나면 자식 뷰 정리
            if (_views == null) return;
            foreach (var v in _views) if (v != null) Destroy(v);
        }
    }
}
