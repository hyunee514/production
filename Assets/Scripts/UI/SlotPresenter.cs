using System;
using System.Globalization;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    public enum SlotState { Empty, Ok, RecoveredFromBackup, Corrupt }
    public enum SlotMode { Save, Load }
    public enum SlotActionKind { None, Blocked, SaveNow, ConfirmOverwrite, LoadNow, ConfirmLoad }

    /// <summary>슬롯 한 칸에 표시할 문구와 가능한 동작 (화면 표시 전용 값).</summary>
    public sealed class SlotViewModel
    {
        public int slot;
        public bool isAuto;
        public SlotState state;
        public string title;            // "자동 저장" / "저장 1"
        public string islandName;       // 비었거나 손상이면 "-"
        public string playTime;         // "2시간 05분"
        public string savedAt;          // "2026-10-31 18:50"
        public string savedAgo;         // "3분 전"
        public string statusText;       // 정상 / 손상 안내
        public bool gameComplete;
        public bool canLoad;
        public bool canSave;            // 수동 슬롯만 true
    }

    public sealed class SlotAction
    {
        public SlotActionKind kind;
        public string message;
        public SlotAction(SlotActionKind kind, string message = null) { this.kind = kind; this.message = message; }
    }

    /// <summary>SlotSummary(저장 파일 요약)를 화면 문구로 바꾼다 (UnityEngine 의존 없음 -> 테스트 가능).</summary>
    public static class SlotPresenter
    {
        public static string FormatPlayTime(float seconds)
        {
            int total = (int)Math.Max(0f, seconds);
            if (total < 60) return $"{total}초";
            if (total < 3600) return $"{total / 60}분";
            return $"{total / 3600}시간 {(total % 3600) / 60:00}분";
        }

        private static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(iso)) return false;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return false;
            utc = parsed.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : parsed.ToUniversalTime();
            return true;
        }

        /// <param name="toLocal">UTC -> 로컬 시각 변환 (기본: DateTime.ToLocalTime). 테스트에서는 고정 오프셋을 넣는다.</param>
        public static string FormatSavedAt(string iso, Func<DateTime, DateTime> toLocal = null)
        {
            if (!TryParseUtc(iso, out var utc)) return "-";
            var local = (toLocal ?? (d => d.ToLocalTime()))(utc);
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FormatAgo(string iso, DateTime utcNow)
        {
            if (!TryParseUtc(iso, out var utc)) return "";
            var diff = utcNow - utc;
            if (diff.TotalMinutes < 1) return "방금 전";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}시간 전";
            return $"{(int)diff.TotalDays}일 전";
        }

        public static SlotViewModel Build(SlotSummary s, Func<string, string> islandName, DateTime utcNow, Func<DateTime, DateTime> toLocal = null)
        {
            var vm = new SlotViewModel
            {
                slot = s.slot,
                isAuto = s.slot == SaveSystem.AutoSlot,
                title = s.slot == SaveSystem.AutoSlot ? "자동 저장" : $"저장 {s.slot}",
                islandName = "-", playTime = "-", savedAt = "-", savedAgo = "",
                canSave = s.slot != SaveSystem.AutoSlot,
            };

            if (!s.exists)
            {
                vm.state = SlotState.Empty;
                vm.statusText = "비어 있음";
                return vm;
            }

            if (!s.valid)
            {
                vm.state = SlotState.Corrupt;
                vm.statusText = "파일이 손상되어 불러올 수 없습니다";
                return vm;
            }

            vm.state = s.usedBackup ? SlotState.RecoveredFromBackup : SlotState.Ok;
            vm.canLoad = true;
            vm.gameComplete = s.gameComplete;
            vm.islandName = string.IsNullOrEmpty(s.islandId) ? "-" : (islandName != null ? islandName(s.islandId) : s.islandId);
            vm.playTime = FormatPlayTime(s.playTimeSeconds);
            vm.savedAt = FormatSavedAt(s.savedAtUtc, toLocal);
            vm.savedAgo = FormatAgo(s.savedAtUtc, utcNow);
            vm.statusText = s.usedBackup ? "본 파일이 손상되어 직전 저장본으로 불러옵니다"
                          : s.gameComplete ? "게임 클리어" : "정상";
            return vm;
        }
    }

    /// <summary>슬롯을 눌렀을 때 무엇을 할지 결정한다. 덮어쓰기·불러오기는 한 번 더 눌러야 실행된다 (실수 방지).</summary>
    public static class SlotActionPlanner
    {
        /// <param name="armed">같은 슬롯을 직전에 눌러 확인 대기 중인가</param>
        public static SlotAction Decide(SlotMode mode, SlotViewModel vm, bool armed, bool confirmLoad)
        {
            if (mode == SlotMode.Load)
            {
                if (!vm.canLoad)
                    return new SlotAction(SlotActionKind.Blocked, vm.state == SlotState.Corrupt ? "손상된 파일은 불러올 수 없습니다." : "비어 있는 슬롯입니다.");
                if (confirmLoad && !armed)
                    return new SlotAction(SlotActionKind.ConfirmLoad, "저장하지 않은 진행은 사라집니다. 한 번 더 누르면 불러옵니다.");
                return new SlotAction(SlotActionKind.LoadNow);
            }

            // Save
            if (!vm.canSave) return new SlotAction(SlotActionKind.Blocked, "자동 저장 칸에는 직접 저장할 수 없습니다.");
            if (vm.state == SlotState.Empty) return new SlotAction(SlotActionKind.SaveNow);
            if (!armed)
                return new SlotAction(SlotActionKind.ConfirmOverwrite,
                    vm.state == SlotState.Corrupt ? "손상된 파일을 새 저장으로 바꿉니다. 한 번 더 누르면 저장합니다."
                                                  : "기존 저장을 덮어씁니다. 한 번 더 누르면 저장합니다.");
            return new SlotAction(SlotActionKind.SaveNow);
        }
    }
}
