using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoSurvival.Save;

namespace AutoSurvival.UI
{
    /// <summary>
    /// "한 번 더 누르면 실행" 확인 장치. 처음 누르면 준비(Arm)만 하고 false, 제한 시간 안에 같은 키로 다시 누르면 true.
    /// 다른 키를 누르면 이전 준비는 취소된다. (새 게임 덮어쓰기, 종료 확인에 사용)
    /// </summary>
    public sealed class ConfirmGate
    {
        private string _key;
        private float _until;

        public float WindowSeconds { get; set; } = 3.5f;

        public bool Press(string key, float now)
        {
            if (_key == key && now <= _until) { _key = null; return true; }
            _key = key; _until = now + WindowSeconds;
            return false;
        }

        public bool IsArmed(string key, float now) => _key == key && now <= _until;
        public void Cancel() { _key = null; }
    }

    /// <summary>메인 메뉴가 저장 슬롯 목록을 보고 내리는 판단들 (엔진 비의존).</summary>
    public static class MainMenuModel
    {
        /// <summary>이어할 수 있는 저장인가 (정상이거나, 본 파일이 깨졌어도 직전 저장본으로 복구 가능)</summary>
        public static bool IsUsable(SlotSummary s) => s != null && s.exists && (s.valid || s.usedBackup);

        public static bool HasAnyUsableSave(IEnumerable<SlotSummary> slots) => slots != null && slots.Any(IsUsable);

        /// <summary>가장 최근에 저장된 슬롯. 저장 시각을 읽을 수 없는 것은 가장 오래된 것으로 취급한다.</summary>
        public static SlotSummary Latest(IEnumerable<SlotSummary> slots)
        {
            if (slots == null) return null;
            SlotSummary best = null; DateTime bestTime = DateTime.MinValue;
            foreach (var s in slots)
            {
                if (!IsUsable(s)) continue;
                var t = Parse(s.savedAtUtc);
                if (best == null || t > bestTime) { best = s; bestTime = t; }
            }
            return best;
        }

        public static DateTime Parse(string iso)
        {
            if (!string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) return t;
            return DateTime.MinValue;
        }

        /// <summary>예: "마지막 저장: 불시착 해안 · 12분 · 3분 전". 저장이 없으면 안내 문구.</summary>
        public static string DescribeLatest(SlotSummary latest, Func<string, string> islandName, DateTime utcNow)
        {
            if (latest == null) return "저장된 게임이 없어요. 새 게임으로 시작하자!";
            string island = islandName != null ? islandName(latest.islandId) : latest.islandId;
            string ago = SlotPresenter.FormatAgo(latest.savedAtUtc, utcNow);
            string text = $"마지막 저장: {island} / 플레이 {SlotPresenter.FormatPlayTime(latest.playTimeSeconds)}";
            if (!string.IsNullOrEmpty(ago)) text += $" / {ago}";
            if (latest.usedBackup) text += " (복구본)";
            return text;
        }
    }
}
